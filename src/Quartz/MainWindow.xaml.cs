using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CefSharp;
using CefSharp.Wpf;
using Microsoft.Win32;
using Quartz.Models;
using Quartz.Services;
using QuartzDownloadItem = Quartz.Models.DownloadItem;

namespace Quartz;

public partial class MainWindow : Window
{
    private static readonly ImageSource DefaultTabIcon = LoadDefaultTabIcon();
    private static readonly HttpClient FaviconClient = CreateFaviconClient();
    private readonly bool _isPrivate;
    private readonly string? _privateDataDirectory;
    private readonly List<BrowserTab> _tabs = [];
    private readonly BookmarkService _bookmarkService;
    private readonly DownloadService _downloadService;
    private readonly HistoryService _historyService;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updateService;
    private readonly ExtensionService _extensionService;
    private readonly IRequestContext _requestContext;
    private readonly bool _ownsRequestContext;
    private const int TabsPerRow = 4;
    private BrowserTab? _activeTab;
    private bool _isPopulatingSettings;
    private int _visibleTabRow;
    private readonly HashSet<string> _reportedUnsupportedExtensionUrls = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow() : this(false) { }

    public MainWindow(bool isPrivate)
    {
        _isPrivate = isPrivate;
        _privateDataDirectory = isPrivate
            ? Path.Combine(Path.GetTempPath(), $"Quartz-Private-{Guid.NewGuid():N}")
            : null;
        InitializeComponent();
        if (isPrivate)
        {
            // An empty CEF cache path creates an in-memory, isolated request context.
            _requestContext = new RequestContext(new RequestContextSettings());
            _ownsRequestContext = true;
        }
        else
        {
            _requestContext = Cef.GetGlobalRequestContext();
        }
        _bookmarkService = BookmarkService.CreateDefault();
        _downloadService = isPrivate
            ? DownloadService.CreatePrivate(_privateDataDirectory!)
            : DownloadService.CreateDefault();
        _historyService = isPrivate
            ? HistoryService.CreatePrivate(_privateDataDirectory!)
            : HistoryService.CreateDefault();
        _settingsService = SettingsService.CreateDefault();
        _updateService = UpdateService.CreateDefault();
        _extensionService = ExtensionService.CreateDefault();
        ThemeManager.AppearanceChanged += ThemeManager_AppearanceChanged;
        BookmarksItems.ItemsSource = _bookmarkService.Bookmarks;
        HistoryItems.ItemsSource = _historyService.Entries;
        DownloadsItems.ItemsSource = _downloadService.Downloads;
        ExtensionsItems.ItemsSource = _extensionService.Extensions;
        _downloadService.PersistenceFailed += DownloadService_PersistenceFailed;
        PopulateSettingsControls();
        RefreshUpdateControls();
        ApplySidebarVisibility(_settingsService.Current.SidebarVisible);
        UpdateBookmarkButton();
        if (_isPrivate)
        {
            Title = "Private Window - Quartz";
            StatusText.Text = "Private browsing · history is not saved";
            PrivateModeBadge.Visibility = Visibility.Visible;
            ToolbarBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("QuartzPrivateBrush");
            DownloadsHeading.Text = "Private Downloads";
            PrivateDownloadNotice.Visibility = Visibility.Visible;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateMaximizeRestoreButton();

        var startupAddress = _settingsService.Current.StartupBehavior == StartupBehavior.BlankPage
            ? BrowserSettings.NewTabPage
            : _settingsService.Current.HomepageUrl;
        await CreateTabAsync(startupAddress);
        if (!_isPrivate && _settingsService.Current.AutoCheckForUpdates)
        {
            _ = CheckForUpdatesAsync(quiet: true);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButton();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (WindowState == WindowState.Minimized || TabStripBorder is null)
        {
            return;
        }

        var point = e.GetPosition(TabStripBorder);
        var inTabStrip = point.X >= 0 && point.Y >= 0 && point.X <= TabStripBorder.ActualWidth && point.Y <= TabStripBorder.ActualHeight;
        if (!inTabStrip || IsTabStripControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // The window can transition state between the hit test and DragMove.
        }
    }

    private static bool IsTabStripControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or TabItem || source is FrameworkElement { Tag: BrowserTab })
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (MaximizeRestoreWindowButton is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreWindowButton.Content = isMaximized ? "\u2750" : "\u25A1";
        MaximizeRestoreWindowButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }

    private Task CreateTabAsync(string? initialAddress = null, bool selectAddressBar = false)
    {
        var browser = new ChromiumWebBrowser
        {
            RequestContext = _requestContext,
            BrowserSettings = new CefSharp.BrowserSettings
            {
                Javascript = CefState.Enabled,
                ImageLoading = CefState.Enabled,
                LocalStorage = CefState.Enabled,
                WebGl = CefState.Enabled
            }
        };
        var tab = new BrowserTab(browser);
        tab.HeaderItem = CreateTabHeader(tab);

        ConfigureBrowser(tab);
        tab.IsInitialized = true;

        _tabs.Add(tab);
        RefreshTabStrip();
        SetActiveTab(tab);

        try
        {
            SetLoadingState(true);
            StatusText.Text = "Starting browser engine...";
            Navigate(tab, initialAddress ?? BrowserSettings.NewTabPage);

            if (selectAddressBar)
            {
                AddressBar.Focus();
                AddressBar.SelectAll();
            }
        }
        catch (Exception exception)
        {
            if (tab.IsClosed)
            {
                return Task.CompletedTask;
            }

            tab.IsLoading = false;
            tab.Status = "Quartz could not start the browser engine.";
            SetLoadingState(false);
            StatusText.Text = tab.Status;
            MessageBox.Show(
                $"Quartz could not start its Chromium/CEF engine.\n\n{exception.Message}",
                "Quartz startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private Border CreateTabHeader(BrowserTab tab)
    {
        var favicon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 7, 0),
            Source = DefaultTabIcon,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "New tab",
            MinWidth = 0,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "\u00D7",
            Margin = new Thickness(5, 0, 0, 0),
            Style = (Style)FindResource("TabCloseButtonStyle"),
            ToolTip = "Close tab (Ctrl+W)"
        };
        closeButton.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };

        tab.TitleBlock = title;
        tab.FaviconImage = favicon;

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(favicon, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(closeButton, 2);
        content.Children.Add(favicon);
        content.Children.Add(title);
        content.Children.Add(closeButton);

        var header = new Border
        {
            Tag = tab,
            Height = 32,
            Margin = new Thickness(1),
            Padding = new Thickness(9, 0, 6, 0),
            CornerRadius = new CornerRadius(8, 8, 3, 3),
            BorderThickness = new Thickness(1),
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = "Switch tab"
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SetActiveTab(tab);
                e.Handled = true;
            }
        };
        header.MouseEnter += (_, _) =>
        {
            if (tab != _activeTab)
            {
                header.SetResourceReference(Border.BackgroundProperty, "QuartzHoverBrush");
            }
        };
        header.MouseLeave += (_, _) => UpdateTabHeaderStyles();
        return header;
    }

    private void ConfigureBrowser(BrowserTab tab)
    {
        tab.Browser.DownloadHandler = new CefDownloadHandler(
            _downloadService,
            Dispatcher,
            status =>
            {
                StatusText.Text = status;
                ShowDownloadsPanel();
            });
        tab.Browser.PermissionHandler = new CefPermissionHandler(this, Dispatcher);
        tab.Browser.RequestHandler = new CefRequestHandler(
            Dispatcher,
            (url, error) => ShowCertificateWarning(tab, url, error),
            url => HandleUnsupportedExtensionNavigation(tab, url));
        tab.Browser.LifeSpanHandler = new CefLifeSpanHandler(
            Dispatcher,
            url => _ = CreateTabAsync(url));
        tab.Browser.DisplayHandler = new CefDisplayHandler(url =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tab.FaviconUrl = url;
                _ = UpdateTabFaviconAsync(tab);
            })));

        tab.Browser.FrameLoadStart += (_, e) =>
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                tab.LastLoadFailed = false;
                if (!tab.IsRenderingNewTabPage)
                {
                    tab.IsNewTabPage = false;
                }

                tab.IsLoading = true;
                tab.FaviconUrl = null;
                tab.FaviconImage.Source = DefaultTabIcon;
                tab.Status = $"Loading {e.Url}";
                if (tab == _activeTab)
                {
                    SetLoadingState(true);
                    StatusText.Text = tab.Status;
                }
            }));
        };

        tab.Browser.LoadError += (_, e) =>
        {
            if (!e.Frame.IsMain || e.ErrorCode == CefErrorCode.Aborted)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                tab.LastLoadFailed = true;
                tab.Status = $"This page could not be loaded ({e.ErrorCode}).";
                if (tab == _activeTab)
                {
                    StatusText.Text = tab.Status;
                    MessageBox.Show(
                        tab.Status + $"\n\n{e.FailedUrl}\n{e.ErrorText}",
                        "Quartz navigation error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }));
        };

        tab.Browser.LoadingStateChanged += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
        {
            tab.IsLoading = e.IsLoading;
            if (!e.IsLoading)
            {
                if (tab.IsRenderingNewTabPage)
                {
                    tab.IsRenderingNewTabPage = false;
                    tab.IsNewTabPage = true;
                    tab.Status = "Quartz new tab";
                }
                else if (!tab.LastLoadFailed)
                {
                    tab.Status = "Done";
                    RecordSuccessfulNavigation(tab);
                    _ = UpdateTabFaviconAsync(tab);
                }
            }

            if (tab == _activeTab)
            {
                SetLoadingState(e.IsLoading);
                UpdateNavigationButtons();
                UpdateAddressBar();
                UpdateBookmarkButton();
                UpdateSecurityIndicator();
                UpdateSiteInfoPanel();
                StatusText.Text = tab.Status;
            }
        }));

        tab.Browser.AddressChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tab == _activeTab)
            {
                UpdateAddressBar();
                UpdateBookmarkButton();
                UpdateSecurityIndicator();
                UpdateSiteInfoPanel();
            }
        }));

        tab.Browser.TitleChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            var browserTitle = tab.Browser.Title;
            tab.Title = tab.IsNewTabPage || string.IsNullOrWhiteSpace(browserTitle) ? "New tab" : browserTitle;
            tab.TitleBlock.Text = tab.Title;
            if (TabManagerOverlay.Visibility == Visibility.Visible)
            {
                RenderTabManagerCards();
            }
            if (tab == _activeTab)
            {
                UpdateWindowTitle();
            }
        }));

        tab.Browser.JavascriptMessageReceived += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tab.IsNewTabPage && e.Message is string input && !string.IsNullOrWhiteSpace(input))
            {
                Navigate(tab, input);
            }
        }));
    }

    private void HandleUnsupportedExtensionNavigation(BrowserTab tab, string url)
    {
        try
        {
            StatusText.Text = "Chrome Web Store installation was blocked safely. Use Extensions for compatible ZIP or unpacked extensions.";
            if (!_reportedUnsupportedExtensionUrls.Add(url))
            {
                return;
            }

            var choice = MessageBox.Show(
                "Chrome Web Store direct installation is not supported yet in Quartz.\n\nQuartz supports compatible unpacked Chromium extensions and direct .zip packages. Chrome Web Store install APIs require Google Chrome and may not work in CEF.\n\nYes: open Extensions Manager\nNo: copy this request URL\nCancel: stay on this page",
                "Chrome Web Store limitation",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                ToggleExtensionsPanel();
            }
            else if (choice == MessageBoxResult.No)
            {
                try { Clipboard.SetText(url); StatusText.Text = "Blocked Chrome Web Store URL copied to the clipboard."; }
                catch (Exception exception) { QuartzLog.Error("Copy blocked extension URL", exception); }
            }
        }
        catch (Exception exception)
        {
            QuartzLog.Error("Unsupported extension navigation", exception);
        }
    }

    private void ShowCertificateWarning(BrowserTab tab, string url, CefErrorCode error)
    {
        tab.LastLoadFailed = true;
        tab.Status = "Quartz blocked a connection whose certificate could not be verified.";
        if (tab != _activeTab)
        {
            return;
        }

        StatusText.Text = tab.Status;
        MessageBox.Show(
            $"{tab.Status}\n\n{url}\n{error}",
            "Quartz security warning",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task UpdateTabFaviconAsync(BrowserTab tab)
    {
        if (tab.IsClosed || !tab.IsInitialized || tab.IsNewTabPage)
        {
            tab.FaviconImage.Source = DefaultTabIcon;
            return;
        }

        var expectedSource = tab.Browser.Address;
        ImageSource? favicon = null;
        var faviconUrl = tab.FaviconUrl;

        if (Uri.TryCreate(faviconUrl, UriKind.Absolute, out var faviconUri) &&
            (faviconUri.Scheme == Uri.UriSchemeHttp || faviconUri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var bytes = await FaviconClient.GetByteArrayAsync(faviconUri);
                using var stream = new MemoryStream(bytes, writable: false);
                favicon = CreateFaviconImage(stream);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or IOException or ArgumentException or NotSupportedException)
            {
            }
        }

        if (favicon is null && Uri.TryCreate(expectedSource, UriKind.Absolute, out var pageUri) &&
            (pageUri.Scheme == Uri.UriSchemeHttp || pageUri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var fallbackUri = new Uri(pageUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico");
                var bytes = await FaviconClient.GetByteArrayAsync(fallbackUri);
                using var fallbackStream = new MemoryStream(bytes, writable: false);
                favicon = CreateFaviconImage(fallbackStream);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or IOException or ArgumentException or NotSupportedException)
            {
            }
        }

        if (tab.IsClosed || !string.Equals(expectedSource, tab.Browser.Address, StringComparison.Ordinal))
        {
            return;
        }

        tab.FaviconImage.Source = favicon ?? DefaultTabIcon;
        if (TabManagerOverlay.Visibility == Visibility.Visible)
        {
            RenderTabManagerCards();
        }
    }

    private static ImageSource CreateFaviconImage(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 16;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static HttpClient CreateFaviconClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Quartz/0.10");
        return client;
    }

    private static ImageSource LoadDefaultTabIcon()
    {
        var bitmap = BitmapFrame.Create(
            new Uri("pack://application:,,,/Assets/Quartz.ico", UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    private void Navigate(BrowserTab tab, string input)
    {
        if (!tab.IsInitialized)
        {
            return;
        }

        if (string.Equals(input?.Trim(), BrowserSettings.NewTabPage, StringComparison.OrdinalIgnoreCase))
        {
            ShowNewTabPage(tab);
            return;
        }

        var destination = AddressInterpreter.ToNavigationUri(
            input,
            _settingsService.Current.SearchEngine);
        if (destination is null)
        {
            return;
        }

        tab.IsNewTabPage = false;

        if (tab == _activeTab && !AddressBar.IsKeyboardFocusWithin)
        {
            AddressBar.Text = destination.AbsoluteUri;
        }

        tab.Browser.Load(destination.AbsoluteUri);
    }

    private void ShowNewTabPage(BrowserTab tab)
    {
        if (!tab.IsInitialized || tab.IsClosed)
        {
            return;
        }

        tab.IsNewTabPage = true;
        tab.IsRenderingNewTabPage = true;
        tab.Title = "New tab";
        tab.TitleBlock.Text = tab.Title;
        tab.Status = "Quartz new tab";
        tab.Browser.LoadHtml(
            NewTabPageBuilder.Build(_bookmarkService.Bookmarks),
            "https://quartz.local/newtab/");

        if (tab == _activeTab)
        {
            AddressBar.Clear();
            UpdateWindowTitle();
            UpdateSecurityIndicator();
            StatusText.Text = tab.Status;
        }
    }

    private void RefreshNewTabPages()
    {
        foreach (var tab in _tabs.Where(tab => tab.IsNewTabPage && tab.IsInitialized && !tab.IsClosed))
        {
            ShowNewTabPage(tab);
        }
    }

    private void SetActiveTab(BrowserTab? tab)
    {
        _activeTab = tab;
        EnsureActiveTabRowIsVisible();
        BrowserHost.Children.Clear();

        if (tab is not null)
        {
            BrowserHost.Children.Add(tab.Browser);
        }

        UpdateAddressBar(force: true);
        UpdateNavigationButtons();
        UpdateWindowTitle();
        UpdateBookmarkButton();
        UpdateSecurityIndicator();
        UpdateSiteInfoPanel();
        SetLoadingState(tab?.IsLoading == true);
        StatusText.Text = tab?.Status ?? "No tabs open";
        UpdateTabHeaderStyles();
        if (TabManagerOverlay.Visibility == Visibility.Visible)
        {
            RenderTabManagerCards();
        }
    }

    private void UpdateTabHeaderStyles()
    {
        foreach (var browserTab in _tabs)
        {
            if (browserTab.HeaderItem is not Border header)
            {
                continue;
            }

            var isActive = browserTab == _activeTab;
            header.SetResourceReference(Border.BackgroundProperty, isActive ? "QuartzSurfaceBrush" : "QuartzTabStripBrush");
            header.SetResourceReference(Border.BorderBrushProperty, isActive ? "QuartzAccentBrush" : "QuartzBorderBrush");
            header.BorderThickness = isActive ? new Thickness(1, 1, 1, 2) : new Thickness(1);
            browserTab.TitleBlock.SetResourceReference(TextBlock.ForegroundProperty, isActive ? "QuartzTextBrush" : "QuartzMutedTextBrush");
            browserTab.TitleBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void RefreshTabStrip()
    {
        var rowCount = Math.Max(1, (int)Math.Ceiling(_tabs.Count / (double)TabsPerRow));
        _visibleTabRow = Math.Clamp(_visibleTabRow, 0, rowCount - 1);

        TabStripItems.Items.Clear();
        foreach (var tab in _tabs.Skip(_visibleTabRow * TabsPerRow).Take(TabsPerRow))
        {
            TabStripItems.Items.Add(tab.HeaderItem);
        }

        TabRowUpButton.IsEnabled = _visibleTabRow > 0;
        TabRowDownButton.IsEnabled = _visibleTabRow < rowCount - 1;
        UpdateTabHeaderStyles();
    }

    private void EnsureActiveTabRowIsVisible()
    {
        if (_activeTab is null)
        {
            return;
        }

        var activeIndex = _tabs.IndexOf(_activeTab);
        if (activeIndex < 0)
        {
            return;
        }

        var activeRow = activeIndex / TabsPerRow;
        if (activeRow != _visibleTabRow)
        {
            _visibleTabRow = activeRow;
            RefreshTabStrip();
        }
    }

    private void TabRowUpButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTabRow(_visibleTabRow - 1);
    }

    private void TabRowDownButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTabRow(_visibleTabRow + 1);
    }

    private void ShowTabRow(int row)
    {
        var rowCount = Math.Max(1, (int)Math.Ceiling(_tabs.Count / (double)TabsPerRow));
        var clampedRow = Math.Clamp(row, 0, rowCount - 1);
        if (clampedRow == _visibleTabRow)
        {
            return;
        }

        _visibleTabRow = clampedRow;
        RefreshTabStrip();
    }

    private void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = tab == _activeTab;
        BrowserHost.Children.Remove(tab.Browser);
        _tabs.RemoveAt(index);
        tab.Dispose();
        RefreshTabStrip();

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            _ = CreateTabAsync(BrowserSettings.NewTabPage, selectAddressBar: true);
            return;
        }

        if (wasActive)
        {
            var nextIndex = Math.Min(index, _tabs.Count - 1);
            SetActiveTab(_tabs[nextIndex]);
        }

        if (TabManagerOverlay.Visibility == Visibility.Visible)
        {
            RenderTabManagerCards();
        }
    }

    private void UpdateAddressBar(bool force = false)
    {
        if (_activeTab is null || (!force && AddressBar.IsKeyboardFocusWithin))
        {
            return;
        }

        AddressBar.Text = _activeTab.IsNewTabPage
            ? string.Empty
            : _activeTab.Browser.Address ?? string.Empty;
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _activeTab?.Browser.CanGoBack == true;
        ForwardButton.IsEnabled = _activeTab?.Browser.CanGoForward == true;
    }

    private void UpdateWindowTitle()
    {
        var pageTitle = _activeTab?.Title;
        var suffix = _isPrivate ? " - Private Quartz" : " - Quartz";
        Title = string.IsNullOrWhiteSpace(pageTitle) ? $"Quartz{suffix}" : $"{pageTitle}{suffix}";
    }

    private void UpdateBookmarkButton()
    {
        var currentUrl = GetActivePageUrl();
        var isBookmarked = currentUrl is not null && _bookmarkService.IsBookmarked(currentUrl);

        BookmarkButton.IsEnabled = currentUrl is not null;
        BookmarkButton.Content = isBookmarked ? "\u2605" : "\u2606";
        BookmarkButton.ToolTip = isBookmarked
            ? "Remove bookmark (Ctrl+D)"
            : "Bookmark this page (Ctrl+D)";
    }

    private void UpdateSecurityIndicator()
    {
        var uri = GetCurrentPageUri();
        if (uri?.Scheme == Uri.UriSchemeHttps)
        {
            SecurityButton.Content = "Secure";
            SecurityButton.Foreground = (System.Windows.Media.Brush)FindResource("QuartzSecureBrush");
            SecurityButton.ToolTip = "Secure HTTPS connection · click for site information";
        }
        else if (uri?.Scheme == Uri.UriSchemeHttp)
        {
            SecurityButton.Content = "Not secure";
            SecurityButton.Foreground = (System.Windows.Media.Brush)FindResource("QuartzDangerBrush");
            SecurityButton.ToolTip = "Unencrypted HTTP connection · click for site information";
        }
        else
        {
            SecurityButton.Content = "Local";
            SecurityButton.Foreground = (System.Windows.Media.Brush)FindResource("QuartzMutedTextBrush");
            SecurityButton.ToolTip = "Local or internal page · click for site information";
        }
    }

    private void UpdateSiteInfoPanel()
    {
        var uri = GetCurrentPageUri();
        var isWebPage = uri is not null &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        SiteDomainText.Text = isWebPage ? uri!.Host : "Local or internal page";
        SiteUrlText.Text = uri?.AbsoluteUri ?? string.Empty;
        ClearCurrentSiteDataButton.IsEnabled = isWebPage && _activeTab?.IsInitialized == true;

        if (uri?.Scheme == Uri.UriSchemeHttps)
        {
            SiteConnectionText.Text = "Secure HTTPS connection";
            SiteConnectionText.Foreground = (System.Windows.Media.Brush)FindResource("QuartzSecureBrush");
            SitePrivacyNoteText.Text = "The connection is encrypted. The site may still store cookies and other data in this browser profile.";
        }
        else if (uri?.Scheme == Uri.UriSchemeHttp)
        {
            SiteConnectionText.Text = "Not secure HTTP connection";
            SiteConnectionText.Foreground = (System.Windows.Media.Brush)FindResource("QuartzDangerBrush");
            SitePrivacyNoteText.Text = "This connection is not encrypted. Avoid entering sensitive information on this page.";
        }
        else
        {
            SiteConnectionText.Text = "Local or internal page";
            SiteConnectionText.Foreground = (System.Windows.Media.Brush)FindResource("QuartzMutedTextBrush");
            SitePrivacyNoteText.Text = "This page is provided by Quartz or Chromium and does not use a normal website connection.";
        }
    }

    private Uri? GetCurrentPageUri()
    {
        if (_activeTab?.IsNewTabPage == true)
        {
            return null;
        }

        var source = _activeTab?.Browser.Address;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri : null;
    }

    private string? GetActivePageUrl()
    {
        var source = _activeTab?.IsNewTabPage == true ? null : _activeTab?.Browser.Address;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private void ToggleActivePageBookmark()
    {
        var currentUrl = GetActivePageUrl();
        if (currentUrl is null || _activeTab is null)
        {
            return;
        }

        try
        {
            if (_bookmarkService.IsBookmarked(currentUrl))
            {
                _bookmarkService.Remove(currentUrl);
            }
            else
            {
                var pageTitle = _activeTab.IsLoading ? currentUrl : _activeTab.Title;
                if (string.IsNullOrWhiteSpace(pageTitle) || pageTitle == "New tab")
                {
                    pageTitle = currentUrl;
                }

                _bookmarkService.Add(pageTitle, currentUrl);
            }

            UpdateBookmarkButton();
        }
        catch (IOException exception)
        {
            ShowBookmarkSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowBookmarkSaveError(exception);
        }
    }

    private static void ShowBookmarkSaveError(Exception exception) =>
        MessageBox.Show(
            $"Quartz could not save the bookmarks file.\n\n{exception.Message}",
            "Quartz bookmark error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void RecordSuccessfulNavigation(BrowserTab tab)
    {
        if (_isPrivate || tab.IsNewTabPage)
        {
            return;
        }

        var url = tab.Browser.Address;
        var title = tab.Title;

        try
        {
            _historyService.RecordVisit(title, url, DateTimeOffset.Now);
        }
        catch (IOException exception)
        {
            ShowHistorySaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowHistorySaveError(exception);
        }
    }

    private static void ShowHistorySaveError(Exception exception) =>
        MessageBox.Show(
            $"Quartz could not save the history file.\n\n{exception.Message}",
            "Quartz history error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void ToggleHistoryPanel()
    {
        var willShow = HistoryPanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            SiteInfoPanel.Visibility = Visibility.Collapsed;
            PerformancePanel.Visibility = Visibility.Collapsed;
            ExtensionsPanel.Visibility = Visibility.Collapsed;
            DownloadsPanel.Visibility = Visibility.Collapsed;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        HistoryPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(360) : new GridLength(0);
    }

    private void ToggleSettingsPanel()
    {
        var willShow = SettingsPanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            SiteInfoPanel.Visibility = Visibility.Collapsed;
            PerformancePanel.Visibility = Visibility.Collapsed;
            ExtensionsPanel.Visibility = Visibility.Collapsed;
            DownloadsPanel.Visibility = Visibility.Collapsed;
            SecurityButton.FontWeight = FontWeights.Normal;
            PopulateSettingsControls();
        }

        SettingsPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(420) : new GridLength(0);
    }

    private void ToggleSiteInfoPanel()
    {
        var willShow = SiteInfoPanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            PerformancePanel.Visibility = Visibility.Collapsed;
            ExtensionsPanel.Visibility = Visibility.Collapsed;
            DownloadsPanel.Visibility = Visibility.Collapsed;
            UpdateSiteInfoPanel();
        }

        SiteInfoPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SecurityButton.FontWeight = willShow ? FontWeights.SemiBold : FontWeights.Normal;
        SidePanelColumn.Width = willShow ? new GridLength(360) : new GridLength(0);
    }

    private void PopulateSettingsControls()
    {
        _isPopulatingSettings = true;
        HomepageTextBox.Text = _settingsService.Current.HomepageUrl;
        SelectComboBoxItem(SearchEngineComboBox, _settingsService.Current.SearchEngine.ToString());
        SelectComboBoxItem(StartupBehaviorComboBox, _settingsService.Current.StartupBehavior.ToString());
        SelectComboBoxItem(ThemeComboBox, _settingsService.Current.Theme.ToString());
        SelectComboBoxItem(AccentComboBox, _settingsService.Current.Accent.ToString());
        SidebarVisibleCheckBox.IsChecked = _settingsService.Current.SidebarVisible;
        AutoCheckUpdatesCheckBox.IsChecked = _settingsService.Current.AutoCheckForUpdates;
        _isPopulatingSettings = false;
    }

    private void RefreshUpdateControls()
    {
        var state = _updateService.State;
        CurrentVersionText.Text = $"Current version: {UpdateService.CurrentVersion}";
        LatestVersionText.Text = state.LatestVersion is null
            ? "Latest version: not checked"
            : $"Latest version: {state.LatestVersion}" + (state.LastCheckedAt is null ? string.Empty : $" · checked {state.LastCheckedAt.Value.LocalDateTime:g}");
        UpdateStatusText.Text = state.Status;
        CheckForUpdatesButton.IsEnabled = true;
        DownloadUpdateButton.Visibility = _updateService.HasReadyUpdate ? Visibility.Collapsed :
            Version.TryParse(state.LatestVersion, out var latest) && latest > UpdateService.CurrentVersion ? Visibility.Visible : Visibility.Collapsed;
        RestartToUpdateButton.Visibility = _updateService.HasReadyUpdate ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CheckForUpdatesAsync(bool quiet)
    {
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking GitHub Releases…";
        var available = await _updateService.CheckAsync();
        RefreshUpdateControls();
        if (available && !quiet)
        {
            StatusText.Text = "A Quartz update is available.";
        }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(quiet: false);

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DownloadUpdateButton.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateStatusText.Text = "Downloading update from GitHub…";
            var progress = new Progress<double>(value => UpdateProgressBar.Value = value);
            await _updateService.DownloadLatestAsync(progress);
            StatusText.Text = "Quartz update downloaded. Restart to install it.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            _updateService.State.Status = $"Update download failed: {exception.Message}";
        }
        finally
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            RefreshUpdateControls();
        }
    }

    private void RestartToUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Quartz will close and install the downloaded update. Continue?", "Restart to update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _updateService.StartPendingInstaller();
            Application.Current.Shutdown();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show($"Quartz could not start the update installer.\n\n{exception.Message}", "Quartz update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AutoCheckUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingSettings)
        {
            return;
        }

        try { _settingsService.UpdateAutoCheckForUpdates(AutoCheckUpdatesCheckBox.IsChecked == true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowSettingsSaveError(exception); }
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private static string? GetSelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettings ||
            !Enum.TryParse<BrowserTheme>(GetSelectedTag(ThemeComboBox), out var theme))
        {
            return;
        }

        var recommendedAccent = ThemeManager.GetRecommendedAccent(theme);
        _isPopulatingSettings = true;
        SelectComboBoxItem(AccentComboBox, recommendedAccent.ToString());
        _isPopulatingSettings = false;
        ApplyAndSaveAppearance(theme, recommendedAccent, SidebarVisibleCheckBox.IsChecked == true);
    }

    private void AccentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettings ||
            !Enum.TryParse<BrowserTheme>(GetSelectedTag(ThemeComboBox), out var theme) ||
            !Enum.TryParse<AccentPreset>(GetSelectedTag(AccentComboBox), out var accent))
        {
            return;
        }

        ApplyAndSaveAppearance(theme, accent, SidebarVisibleCheckBox.IsChecked == true);
    }

    private void SidebarVisibleCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingSettings ||
            !Enum.TryParse<BrowserTheme>(GetSelectedTag(ThemeComboBox), out var theme) ||
            !Enum.TryParse<AccentPreset>(GetSelectedTag(AccentComboBox), out var accent))
        {
            return;
        }

        ApplyAndSaveAppearance(theme, accent, SidebarVisibleCheckBox.IsChecked == true);
    }

    private void ApplyAndSaveAppearance(BrowserTheme theme, AccentPreset accent, bool sidebarVisible)
    {
        ThemeManager.Apply(theme, accent);
        ApplySidebarVisibility(sidebarVisible);

        try
        {
            _settingsService.UpdateAppearance(theme, accent, sidebarVisible);
            StatusText.Text = $"{theme} theme applied.";
        }
        catch (IOException exception)
        {
            ShowSettingsSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowSettingsSaveError(exception);
        }
    }

    private void ThemeManager_AppearanceChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ThemeManager_AppearanceChanged(sender, e));
            return;
        }

        UpdateSecurityIndicator();
        RefreshNewTabPages();
    }

    private void ApplySidebarVisibility(bool isVisible)
    {
        SidebarColumn.Width = isVisible ? new GridLength(56) : new GridLength(0);
        SidebarBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarToggleButton.Background = isVisible
            ? (System.Windows.Media.Brush)FindResource("QuartzAccentSoftBrush")
            : System.Windows.Media.Brushes.Transparent;
        SidebarToggleButton.BorderBrush = isVisible
            ? (System.Windows.Media.Brush)FindResource("QuartzAccentBrush")
            : System.Windows.Media.Brushes.Transparent;
    }

    private void TogglePerformancePanel()
    {
        var willShow = PerformancePanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            SiteInfoPanel.Visibility = Visibility.Collapsed;
            DownloadsPanel.Visibility = Visibility.Collapsed;
            ExtensionsPanel.Visibility = Visibility.Collapsed;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        PerformancePanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(360) : new GridLength(0);
    }

    private void ToggleDownloadsPanel()
    {
        var willShow = DownloadsPanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            SiteInfoPanel.Visibility = Visibility.Collapsed;
            PerformancePanel.Visibility = Visibility.Collapsed;
            ExtensionsPanel.Visibility = Visibility.Collapsed;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        DownloadsPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(420) : new GridLength(0);
    }

    private void ToggleExtensionsPanel()
    {
        var willShow = ExtensionsPanel.Visibility != Visibility.Visible;
        if (willShow)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            SiteInfoPanel.Visibility = Visibility.Collapsed;
            PerformancePanel.Visibility = Visibility.Collapsed;
            DownloadsPanel.Visibility = Visibility.Collapsed;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        ExtensionsPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(420) : new GridLength(0);
    }

    private void ShowDownloadsPanel()
    {
        if (DownloadsPanel.Visibility != Visibility.Visible)
        {
            ToggleDownloadsPanel();
        }
    }

    private async Task ClearCurrentSiteDataAsync()
    {
        var tab = _activeTab;
        var uri = GetCurrentPageUri();
        if (tab?.IsInitialized != true || uri is null ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var result = MessageBox.Show(
            $"Clear cookies and accessible site storage for {uri.Host}? You may be signed out.",
            "Clear Quartz site data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var cookieManager = await _requestContext.GetCookieManagerAsync();
            await cookieManager.DeleteCookiesAsync(uri.GetLeftPart(UriPartial.Authority), null);
            await tab.Browser.EvaluateScriptAsync(
                "(async()=>{localStorage.clear();sessionStorage.clear();" +
                "if(self.caches){for(const k of await caches.keys())await caches.delete(k);}" +
                "if(indexedDB.databases){for(const d of await indexedDB.databases())if(d.name)indexedDB.deleteDatabase(d.name);}" +
                "if(navigator.serviceWorker){for(const r of await navigator.serviceWorker.getRegistrations())await r.unregister();}})();");
            StatusText.Text = $"Site data cleared for {uri.Host}.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            MessageBox.Show(
                $"Quartz could not clear all accessible data for this site.\n\n{exception.Message}",
                "Quartz site data",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ClearProfileSiteDataAsync()
    {
        if (_activeTab is null)
        {
            return;
        }

        if (!ConfirmClear(
                "Clear cookies and site storage for this Quartz profile? You may be signed out of websites.",
                "Clear Quartz cookies and site data"))
        {
            return;
        }

        try
        {
            var cookieManager = await _requestContext.GetCookieManagerAsync();
            await cookieManager.DeleteCookiesAsync();
            using (var callback = new TaskCompletionCallback())
            {
                _requestContext.ClearHttpCache(callback);
                await callback.Task;
            }
            foreach (var tab in _tabs.Where(item => item.IsInitialized && !item.IsClosed))
            {
                await tab.Browser.EvaluateScriptAsync(
                    "(()=>{try{localStorage.clear();sessionStorage.clear();}catch{}})();");
            }
            StatusText.Text = "Cookies and site data cleared.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            MessageBox.Show(
                $"Quartz could not clear the browser profile's site data.\n\n{exception.Message}",
                "Quartz privacy controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DownloadService_PersistenceFailed(object? sender, Exception exception) =>
        MessageBox.Show(
            $"The file downloaded, but Quartz could not save the downloads history.\n\n{exception.Message}",
            "Quartz download history error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void SetLoadingState(bool isLoading)
    {
        var visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        LoadingProgress.Visibility = visibility;
        LoadingText.Visibility = isLoading && ActualWidth >= 1000
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BookmarksLabel.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        if (_activeTab is not null)
        {
            SetLoadingState(_activeTab.IsLoading);
        }
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTab is null)
        {
            return;
        }

        Navigate(_activeTab, AddressBar.Text);
        _activeTab.Browser.Focus();
        e.Handled = true;
    }

    private void AddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        AddressBar.SelectAll();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Browser.CanGoBack == true)
        {
            _activeTab.Browser.Back();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Browser.CanGoForward == true)
        {
            _activeTab.Browser.Forward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) =>
        _activeTab?.Browser.Reload();

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
        {
            Navigate(_activeTab, _settingsService.Current.HomepageUrl);
        }
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e) =>
        ToggleActivePageBookmark();

    private void SecurityButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSiteInfoPanel();

    private void HistoryButton_Click(object sender, RoutedEventArgs e) =>
        ToggleHistoryPanel();

    private void DownloadsButton_Click(object sender, RoutedEventArgs e) =>
        ToggleDownloadsPanel();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSettingsPanel();

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var isVisible = SidebarBorder.Visibility != Visibility.Visible;
        _isPopulatingSettings = true;
        SidebarVisibleCheckBox.IsChecked = isVisible;
        _isPopulatingSettings = false;

        if (Enum.TryParse<BrowserTheme>(_settingsService.Current.Theme.ToString(), out var theme) &&
            Enum.TryParse<AccentPreset>(_settingsService.Current.Accent.ToString(), out var accent))
        {
            ApplyAndSaveAppearance(theme, accent, isVisible);
        }
    }

    private void SidebarBookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        while (CloseOpenPanel())
        {
        }

        var willShow = BookmarksBarBorder.Visibility != Visibility.Visible;
        BookmarksBarBorder.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        BookmarksBarRow.Height = willShow ? new GridLength(28) : new GridLength(0);
        StatusText.Text = willShow
            ? _bookmarkService.Bookmarks.Count == 0
                ? "Bookmarks bar is open. Add a bookmark with Ctrl+D."
                : "Bookmarks bar is open."
            : "Bookmarks bar hidden.";
    }

    private void PerformanceButton_Click(object sender, RoutedEventArgs e) =>
        TogglePerformancePanel();

    private void ExtensionsButton_Click(object sender, RoutedEventArgs e) =>
        ToggleExtensionsPanel();

    private void NewPrivateWindowButton_Click(object sender, RoutedEventArgs e) =>
        OpenPrivateWindow();

    private static void OpenPrivateWindow() =>
        new MainWindow(isPrivate: true).Show();

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPanel.Visibility == Visibility.Visible)
        {
            ToggleHistoryPanel();
        }
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            ToggleSettingsPanel();
        }
    }

    private void CloseSiteInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (SiteInfoPanel.Visibility == Visibility.Visible)
        {
            ToggleSiteInfoPanel();
        }
    }

    private void ClosePerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (PerformancePanel.Visibility == Visibility.Visible)
        {
            TogglePerformancePanel();
        }
    }

    private void CloseExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionsPanel.Visibility == Visibility.Visible)
        {
            ToggleExtensionsPanel();
        }
    }

    private void LoadUnpackedExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose an unpacked Chromium extension folder",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var extension = _extensionService.AddUnpacked(dialog.FolderName);
            StatusText.Text = $"{extension.Name} added. Restart Quartz to load it.";
            MessageBox.Show(
                $"{extension.Name} was added and will be passed to Chromium when Quartz next starts.\n\n" +
                "CEF extension compatibility is experimental. Extensions that require Chrome's toolbar or Chrome Web Store services may not work.",
                "Quartz extensions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Quartz could not add this unpacked extension.\n\n{exception.Message}",
                "Quartz extensions",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void InstallExtensionFromUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var address = PromptForText("Install extension from URL", "Enter a direct HTTPS/HTTP .zip extension package URL:");
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            MessageBox.Show("Enter a direct HTTP or HTTPS .zip URL.", "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (uri.AbsolutePath.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Quartz does not unpack .crx files yet. Download or export a compatible .zip package, or load an unpacked extension folder.", "Chrome Web Store limitation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Quartz accepts direct .zip extension packages only. Chrome Web Store pages and source archives are not installable packages.", "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show("Extensions can read or change browsing data depending on their permissions. Download and install this package disabled first?", "Install extension", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var temporaryPackage = Path.Combine(Path.GetTempPath(), $"Quartz-extension-{Guid.NewGuid():N}.zip");
        try
        {
            StatusText.Text = "Downloading extension package…";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QuartzBrowser/2.1");
            await using (var source = await client.GetStreamAsync(uri))
            await using (var target = File.Create(temporaryPackage))
            {
                await source.CopyToAsync(target);
            }

            StatusText.Text = "Extracting and validating extension…";
            var extension = _extensionService.InstallZipPackage(temporaryPackage, "ZIP URL", isEnabled: false);
            var permissionNote = string.IsNullOrWhiteSpace(extension.Permissions) ? "No manifest permissions were listed." : $"Permissions: {extension.Permissions}";
            if (MessageBox.Show($"{extension.Name} was installed disabled.\n\n{permissionNote}\n\nEnable it when Quartz next restarts?", "Enable extension", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _extensionService.SetEnabled(extension, true);
            }

            StatusText.Text = $"{extension.Name} installed. Restart Quartz to apply extension changes.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Quartz could not install this extension package.\n\n{exception.Message}", "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "Extension installation failed.";
        }
        finally
        {
            if (File.Exists(temporaryPackage)) File.Delete(temporaryPackage);
        }
    }

    private string? PromptForText(string title, string prompt)
    {
        var input = new TextBox { MinWidth = 360, Margin = new Thickness(0, 8, 0, 14) };
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 470,
            Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("QuartzCanvasBrush"),
            Content = new StackPanel { Margin = new Thickness(18), Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, input } }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var install = new Button { Content = "Continue", Width = 80, Style = (Style)FindResource("AccentButtonStyle") };
        install.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);
        ((StackPanel)dialog.Content).Children.Add(buttons);
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void ReloadExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserExtension extension })
        {
            try { _extensionService.AddUnpacked(extension.FolderPath, extension.SourceType, extension.IsEnabled); StatusText.Text = "Extension metadata reloaded. Restart Quartz to apply changes."; }
            catch (Exception exception) when (exception is ArgumentException or IOException or JsonException) { MessageBox.Show(exception.Message, "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    private void OpenExtensionFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserExtension extension } && Directory.Exists(extension.FolderPath))
        {
            Process.Start(new ProcessStartInfo(extension.FolderPath) { UseShellExecute = true });
        }
    }

    private void ExtensionEnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: BrowserExtension extension } checkBox)
        {
            return;
        }

        try
        {
            _extensionService.SetEnabled(extension, checkBox.IsChecked == true);
            StatusText.Text = "Extension setting saved. Restart Quartz to apply it.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(exception.Message, "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserExtension extension })
        {
            return;
        }

        try
        {
            _extensionService.Remove(extension);
            StatusText.Text = $"{extension.Name} removed. Restart Quartz to unload it from this session.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(exception.Message, "Quartz extensions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseDownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsPanel.Visibility == Visibility.Visible)
        {
            ToggleDownloadsPanel();
        }
    }

    private void ClearCompletedDownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.ClearCompleted();
        StatusText.Text = "Completed download records cleared.";
    }

    private static QuartzDownloadItem? GetDownloadItem(object sender) =>
        (sender as FrameworkElement)?.Tag as QuartzDownloadItem;

    private void OpenDownloadedFileButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetDownloadItem(sender);
        if (item is null || !File.Exists(item.SavePath))
        {
            StatusText.Text = "The downloaded file could not be found.";
            return;
        }

        TryOpenDownloadLocation(item.SavePath, "file");
    }

    private void OpenDownloadFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetDownloadItem(sender);
        var folder = item is null ? null : Path.GetDirectoryName(item.SavePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusText.Text = "The download folder could not be found.";
            return;
        }

        TryOpenDownloadLocation(folder, "folder");
    }

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetDownloadItem(sender);
        if (item is not null)
        {
            _downloadService.Cancel(item);
            StatusText.Text = $"Canceling {item.FileName}...";
        }
    }

    private void TryOpenDownloadLocation(string path, string kind)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"Quartz could not open this {kind}.\n\n{exception.Message}",
                "Quartz downloads",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void ClearCurrentSiteDataButton_Click(object sender, RoutedEventArgs e) =>
        await ClearCurrentSiteDataAsync();

    private async void ClearCookiesAndSiteDataButton_Click(object sender, RoutedEventArgs e) =>
        await ClearProfileSiteDataAsync();

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.TryParse<SearchEngine>(GetSelectedTag(SearchEngineComboBox), out var searchEngine) ||
            !Enum.TryParse<StartupBehavior>(GetSelectedTag(StartupBehaviorComboBox), out var startupBehavior) ||
            !Enum.TryParse<BrowserTheme>(GetSelectedTag(ThemeComboBox), out var theme) ||
            !Enum.TryParse<AccentPreset>(GetSelectedTag(AccentComboBox), out var accent))
        {
            ShowSettingsError("Choose a supported search engine and startup behavior.");
            return;
        }

        try
        {
            _settingsService.Update(
                HomepageTextBox.Text,
                searchEngine,
                startupBehavior,
                theme,
                accent,
                SidebarVisibleCheckBox.IsChecked == true);
            ThemeManager.Apply(theme, accent);
            ApplySidebarVisibility(SidebarVisibleCheckBox.IsChecked == true);
            PopulateSettingsControls();
            StatusText.Text = "Settings saved.";
        }
        catch (ArgumentException exception)
        {
            ShowSettingsError(exception.Message);
        }
        catch (IOException exception)
        {
            ShowSettingsSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowSettingsSaveError(exception);
        }
    }

    private static void ShowSettingsError(string message) =>
        MessageBox.Show(
            message,
            "Quartz settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static void ShowSettingsSaveError(Exception exception) =>
        MessageBox.Show(
            $"Quartz could not save the settings file.\n\n{exception.Message}",
            "Quartz settings error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry } && _activeTab is not null)
        {
            Navigate(_activeTab, entry.Url);
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) =>
        ClearHistoryWithConfirmation();

    private void ClearHistoryFromSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ClearHistoryWithConfirmation();

    private void ClearHistoryWithConfirmation()
    {
        if (_historyService.Entries.Count == 0)
        {
            StatusText.Text = "History is already empty.";
            return;
        }

        var result = MessageBox.Show(
            "Clear all browsing history? This cannot be undone.",
            "Clear Quartz history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _historyService.Clear();
            StatusText.Text = "History cleared.";
        }
        catch (IOException exception)
        {
            ShowHistorySaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowHistorySaveError(exception);
        }
    }

    private void ClearDownloadsFromSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_downloadService.Downloads.Any(item => item.IsCompleted))
        {
            StatusText.Text = "Downloads history is already empty.";
            return;
        }

        if (!ConfirmClear(
                "Clear completed downloads history? Downloaded files will not be deleted.",
                "Clear Quartz downloads history"))
        {
            return;
        }

        _downloadService.ClearCompleted();
        StatusText.Text = "Downloads history cleared.";
    }

    private void ClearBookmarksFromSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bookmarkService.Bookmarks.Count == 0)
        {
            StatusText.Text = "Bookmarks are already empty.";
            return;
        }

        if (!ConfirmClear(
                "Clear all bookmarks? This cannot be undone.",
                "Clear Quartz bookmarks"))
        {
            return;
        }

        try
        {
            _bookmarkService.Clear();
            UpdateBookmarkButton();
            StatusText.Text = "Bookmarks cleared.";
        }
        catch (IOException exception)
        {
            ShowBookmarkSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowBookmarkSaveError(exception);
        }
    }

    private void ClearAllBrowsingDataButton_Click(object sender, RoutedEventArgs e)
    {
        var hasData = _historyService.Entries.Count > 0 ||
                      _bookmarkService.Bookmarks.Count > 0 ||
                      _downloadService.Downloads.Any(item => item.IsCompleted);
        if (!hasData)
        {
            StatusText.Text = "Browsing data is already empty.";
            return;
        }

        if (!ConfirmClear(
                "Clear history, completed downloads history, and all bookmarks? This cannot be undone.",
                "Clear all Quartz browsing data"))
        {
            return;
        }

        try
        {
            _historyService.Clear();
            _bookmarkService.Clear();
            _downloadService.ClearCompleted();
            UpdateBookmarkButton();
            StatusText.Text = "Browsing data cleared.";
        }
        catch (IOException exception)
        {
            ShowBrowsingDataClearError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowBrowsingDataClearError(exception);
        }
    }

    private static bool ConfirmClear(string message, string title) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void ShowBrowsingDataClearError(Exception exception) =>
        MessageBox.Show(
            $"Quartz could not clear all browsing data.\n\n{exception.Message}",
            "Quartz data error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Bookmark bookmark } && _activeTab is not null)
        {
            Navigate(_activeTab, bookmark.Url);
        }
    }

    private void RemoveBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: Bookmark bookmark })
        {
            return;
        }

        try
        {
            _bookmarkService.Remove(bookmark.Url);
            UpdateBookmarkButton();
        }
        catch (IOException exception)
        {
            ShowBookmarkSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowBookmarkSaveError(exception);
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) =>
        _ = CreateTabAsync(BrowserSettings.NewTabPage, selectAddressBar: true);

    private void TabManagerButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleTabManager();
    }

    private void CloseTabManagerButton_Click(object sender, RoutedEventArgs e)
    {
        HideTabManager();
    }

    private void ToggleTabManager()
    {
        if (TabManagerOverlay.Visibility == Visibility.Visible)
        {
            HideTabManager();
            return;
        }

        while (CloseOpenPanel())
        {
        }

        RenderTabManagerCards();
        TabManagerOverlay.Visibility = Visibility.Visible;
        TabStripBorder.Visibility = Visibility.Hidden;
    }

    private void HideTabManager()
    {
        TabManagerOverlay.Visibility = Visibility.Collapsed;
        TabStripBorder.Visibility = Visibility.Visible;
    }

    private void RenderTabManagerCards()
    {
        TabManagerItems.Items.Clear();
        TabManagerCount.Text = _tabs.Count == 1 ? "1 open tab" : $"{_tabs.Count} open tabs";

        foreach (var tab in _tabs)
        {
            TabManagerItems.Items.Add(CreateTabManagerCard(tab));
        }
    }

    private Border CreateTabManagerCard(BrowserTab tab)
    {
        var title = new TextBlock
        {
            Text = tab.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = tab.Title
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "QuartzTextBrush");

        var location = new TextBlock
        {
            Text = GetTabLocation(tab),
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = tab.Browser.Address
        };
        location.SetResourceReference(TextBlock.ForegroundProperty, "QuartzMutedTextBrush");

        var favicon = new Image
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 9, 0),
            Stretch = Stretch.Uniform,
            Source = tab.FaviconImage.Source ?? DefaultTabIcon,
            VerticalAlignment = VerticalAlignment.Top
        };

        var closeButton = new Button
        {
            Content = "\u00D7",
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Style = (Style)FindResource("TabCloseButtonStyle"),
            ToolTip = $"Close {tab.Title}"
        };
        closeButton.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
            RenderTabManagerCards();
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(favicon, 0);
        Grid.SetColumn(closeButton, 2);
        row.Children.Add(favicon);
        row.Children.Add(closeButton);
        var text = new StackPanel { MinWidth = 0 };
        text.Children.Add(title);
        text.Children.Add(location);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var previewTitle = new TextBlock
        {
            Text = tab.IsLoading ? "Loading…" : tab.Title,
            Margin = new Thickness(12),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom
            };
        previewTitle.SetResourceReference(TextBlock.ForegroundProperty, "QuartzTextBrush");
        var preview = new Border
        {
            Height = 58,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Child = previewTitle
        };
        preview.SetResourceReference(Border.BackgroundProperty, "QuartzAccentSoftBrush");
        preview.SetResourceReference(Border.BorderBrushProperty, "QuartzBorderBrush");
        preview.BorderThickness = new Thickness(1);

        var content = new StackPanel();
        content.Children.Add(preview);
        content.Children.Add(row);

        var card = new Border
        {
            Width = 240,
            Height = 156,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(9),
            BorderThickness = tab == _activeTab ? new Thickness(2) : new Thickness(1),
            Cursor = Cursors.Hand,
            Child = content,
            ToolTip = "Switch to this tab"
        };
        card.SetResourceReference(Border.BackgroundProperty, tab == _activeTab ? "QuartzAccentSoftBrush" : "QuartzSurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, tab == _activeTab ? "QuartzAccentBrush" : "QuartzBorderBrush");
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SetActiveTab(tab);
                HideTabManager();
                e.Handled = true;
            }
        };
        return card;
    }

    private static string GetTabLocation(BrowserTab tab)
    {
        if (tab.IsNewTabPage)
        {
            return "Quartz new tab";
        }

        return Uri.TryCreate(tab.Browser.Address, UriKind.Absolute, out var uri)
            ? uri.Host
            : tab.Browser.Address ?? "Local page";
    }

    private void ClearAllTabsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Close all open tabs? Quartz will leave one fresh new tab open.",
                "Clear all tabs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var tab in _tabs.ToArray())
        {
            BrowserHost.Children.Remove(tab.Browser);
            tab.Dispose();
        }

        _tabs.Clear();
        _activeTab = null;
        HideTabManager();
        _ = CreateTabAsync(BrowserSettings.NewTabPage, selectAddressBar: true);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && TabManagerOverlay.Visibility == Visibility.Visible)
        {
            HideTabManager();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && CloseOpenPanel())
        {
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            AddressBar.Focus();
            AddressBar.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
        {
            ReloadButton_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            _ = CreateTabAsync(BrowserSettings.NewTabPage, selectAddressBar: true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.A)
        {
            ToggleTabManager();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            ToggleActivePageBookmark();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
        {
            ToggleHistoryPanel();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            ToggleSettingsPanel();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.J)
        {
            ToggleDownloadsPanel();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            OpenPrivateWindow();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W && _activeTab is not null)
        {
            CloseTab(_activeTab);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Left)
        {
            BackButton_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Right)
        {
            ForwardButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private bool CloseOpenPanel()
    {
        if (ExtensionsPanel.Visibility == Visibility.Visible)
        {
            ToggleExtensionsPanel();
            return true;
        }

        if (DownloadsPanel.Visibility == Visibility.Visible)
        {
            ToggleDownloadsPanel();
            return true;
        }

        if (PerformancePanel.Visibility == Visibility.Visible)
        {
            TogglePerformancePanel();
            return true;
        }

        if (SiteInfoPanel.Visibility == Visibility.Visible)
        {
            ToggleSiteInfoPanel();
            return true;
        }

        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            ToggleSettingsPanel();
            return true;
        }

        if (HistoryPanel.Visibility == Visibility.Visible)
        {
            ToggleHistoryPanel();
            return true;
        }

        return false;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        ThemeManager.AppearanceChanged -= ThemeManager_AppearanceChanged;
        _downloadService.PersistenceFailed -= DownloadService_PersistenceFailed;

        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
        if (_ownsRequestContext)
        {
            _requestContext.Dispose();
        }

        if (_isPrivate && IsPrivateDataDirectory(_privateDataDirectory))
        {
            SchedulePrivateDataCleanup(_privateDataDirectory!);
        }
    }

    private static void SchedulePrivateDataCleanup(string path)
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        return;
                    }

                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(500);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    private static bool IsPrivateDataDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        return fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(fullPath).StartsWith("Quartz-Private-", StringComparison.Ordinal);
    }
}
