using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Quartz.Models;
using Quartz.Services;

namespace Quartz;

public partial class MainWindow : Window
{
    private readonly bool _isPrivate;
    private readonly string? _privateDataDirectory;
    private readonly List<BrowserTab> _tabs = [];
    private readonly BookmarkService _bookmarkService;
    private readonly DownloadService _downloadService;
    private readonly HistoryService _historyService;
    private readonly SettingsService _settingsService;
    private Task<CoreWebView2Environment>? _privateEnvironmentTask;
    private DownloadsWindow? _downloadsWindow;
    private BrowserTab? _activeTab;
    private bool _isPopulatingSettings;

    public MainWindow() : this(false) { }

    public MainWindow(bool isPrivate)
    {
        _isPrivate = isPrivate;
        _privateDataDirectory = isPrivate
            ? Path.Combine(Path.GetTempPath(), $"Quartz-Private-{Guid.NewGuid():N}")
            : null;
        InitializeComponent();
        _bookmarkService = BookmarkService.CreateDefault();
        _downloadService = isPrivate
            ? DownloadService.CreatePrivate(_privateDataDirectory!)
            : DownloadService.CreateDefault();
        _historyService = isPrivate
            ? HistoryService.CreatePrivate(_privateDataDirectory!)
            : HistoryService.CreateDefault();
        _settingsService = SettingsService.CreateDefault();
        ThemeManager.AppearanceChanged += ThemeManager_AppearanceChanged;
        BookmarksItems.ItemsSource = _bookmarkService.Bookmarks;
        HistoryItems.ItemsSource = _historyService.Entries;
        _downloadService.PersistenceFailed += DownloadService_PersistenceFailed;
        PopulateSettingsControls();
        ApplySidebarVisibility(_settingsService.Current.SidebarVisible);
        UpdateBookmarkButton();
        if (_isPrivate)
        {
            Title = "Private Window - Quartz";
            StatusText.Text = "Private browsing · history is not saved";
            PrivateModeBadge.Visibility = Visibility.Visible;
            ToolbarBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("QuartzPrivateBrush");
            PrivateWindowButton.Foreground = (System.Windows.Media.Brush)FindResource("QuartzPrivateBrush");
            PrivateWindowButton.ToolTip = "Private profile active. Bookmarks created here are saved normally.";
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);

        var startupAddress = _settingsService.Current.StartupBehavior == StartupBehavior.BlankPage
            ? BrowserSettings.NewTabPage
            : _settingsService.Current.HomepageUrl;
        await CreateTabAsync(startupAddress);
    }

    private async Task CreateTabAsync(string? initialAddress = null, bool selectAddressBar = false)
    {
        var tab = new BrowserTab(new WebView2());
        tab.HeaderItem = CreateTabHeader(tab);

        _tabs.Add(tab);
        Tabs.Items.Add(tab.HeaderItem);
        Tabs.SelectedItem = tab.HeaderItem;
        SetActiveTab(tab);

        try
        {
            SetLoadingState(true);
            StatusText.Text = "Starting browser engine...";

            if (_isPrivate)
            {
                _privateEnvironmentTask ??=
                    CoreWebView2Environment.CreateAsync(userDataFolder: _privateDataDirectory);
            }

            var environment = _privateEnvironmentTask is null
                ? null
                : await _privateEnvironmentTask;
            await tab.Browser.EnsureCoreWebView2Async(environment);

            if (tab.IsClosed)
            {
                return;
            }

            ConfigureBrowser(tab);
            tab.IsInitialized = true;

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
                return;
            }

            tab.IsLoading = false;
            tab.Status = "Quartz could not start the browser engine.";
            SetLoadingState(false);
            StatusText.Text = tab.Status;
            MessageBox.Show(
                $"Quartz could not start WebView2. Make sure the Microsoft Edge WebView2 Runtime is installed.\n\n{exception.Message}",
                "Quartz startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private TabItem CreateTabHeader(BrowserTab tab)
    {
        var title = new TextBlock
        {
            Text = "New tab",
            MinWidth = 120,
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "\u00D7",
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("TabCloseButtonStyle"),
            ToolTip = "Close tab (Ctrl+W)"
        };
        closeButton.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };

        tab.TitleBlock = title;

        return new TabItem
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { title, closeButton }
            },
            Tag = tab
        };
    }

    private void ConfigureBrowser(BrowserTab tab)
    {
        var core = tab.Browser.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;

        core.NavigationStarting += (_, e) =>
        {
            if (!tab.IsRenderingNewTabPage)
            {
                tab.IsNewTabPage = false;
            }

            tab.IsLoading = true;
            tab.Status = $"Loading {e.Uri}";
            if (tab == _activeTab)
            {
                SetLoadingState(true);
                StatusText.Text = tab.Status;
            }
        };

        core.NavigationCompleted += (_, e) =>
        {
            tab.IsLoading = false;

            if (e.IsSuccess)
            {
                if (tab.IsRenderingNewTabPage)
                {
                    tab.IsRenderingNewTabPage = false;
                    tab.IsNewTabPage = true;
                    tab.Status = "Quartz new tab";
                }
                else
                {
                    tab.Status = "Done";
                    RecordSuccessfulNavigation(tab);
                }
            }
            else if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                tab.Status = "Navigation canceled";
            }
            else
            {
                tab.Status = $"This page could not be loaded ({e.WebErrorStatus}).";
            }

            if (tab != _activeTab)
            {
                return;
            }

            SetLoadingState(false);
            UpdateNavigationButtons();
            UpdateAddressBar();
            UpdateBookmarkButton();
            UpdateSecurityIndicator();
            UpdateSiteInfoPanel();
            StatusText.Text = tab.Status;

            if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                return;
            }

            var securityError = IsCertificateError(e.WebErrorStatus);
            MessageBox.Show(
                securityError
                    ? $"Quartz blocked this page because its security certificate could not be verified.\n\n{e.WebErrorStatus}"
                    : tab.Status,
                securityError ? "Quartz security warning" : "Quartz navigation error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        core.SourceChanged += (_, _) =>
        {
            if (tab == _activeTab)
            {
                UpdateAddressBar();
                UpdateBookmarkButton();
                UpdateSecurityIndicator();
                UpdateSiteInfoPanel();
            }
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            var pageTitle = tab.Browser.CoreWebView2.DocumentTitle;
            tab.Title = tab.IsNewTabPage || string.IsNullOrWhiteSpace(pageTitle) ? "New tab" : pageTitle;
            tab.TitleBlock.Text = tab.Title;

            if (tab == _activeTab)
            {
                UpdateWindowTitle();
            }
        };

        core.HistoryChanged += (_, _) =>
        {
            if (tab == _activeTab)
            {
                UpdateNavigationButtons();
            }
        };

        core.DownloadStarting += (_, e) => HandleDownloadStarting(e);
        core.PermissionRequested += (_, e) => HandlePermissionRequested(e);
        core.WebMessageReceived += (_, e) =>
        {
            if (!tab.IsNewTabPage)
            {
                return;
            }

            var input = e.TryGetWebMessageAsString();
            if (!string.IsNullOrWhiteSpace(input))
            {
                Navigate(tab, input);
            }
        };
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            if (tab == _activeTab)
            {
                MessageBox.Show(
                    $"Quartz blocked a connection whose certificate could not be verified.\n\n{e.RequestUri}\n{e.ErrorStatus}",
                    "Quartz security warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(e.Uri))
            {
                _ = CreateTabAsync(e.Uri);
            }
        };
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

        tab.Browser.CoreWebView2.Navigate(destination.AbsoluteUri);
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
        tab.Browser.CoreWebView2.NavigateToString(NewTabPageBuilder.Build(_bookmarkService.Bookmarks));

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
        Tabs.Items.Remove(tab.HeaderItem);
        _tabs.RemoveAt(index);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        if (wasActive)
        {
            var nextIndex = Math.Min(index, _tabs.Count - 1);
            Tabs.SelectedItem = _tabs[nextIndex].HeaderItem;
            SetActiveTab(_tabs[nextIndex]);
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
            : _activeTab.Browser.Source?.AbsoluteUri ?? _activeTab.Browser.CoreWebView2?.Source ?? string.Empty;
    }

    private void UpdateNavigationButtons()
    {
        var core = _activeTab?.Browser.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack == true;
        ForwardButton.IsEnabled = core?.CanGoForward == true;
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
            SecurityButton.Foreground = (System.Windows.Media.Brush)FindResource("QuartzAccentBrush");
            SecurityButton.ToolTip = "Secure HTTPS connection · click for site information";
        }
        else if (uri?.Scheme == Uri.UriSchemeHttp)
        {
            SecurityButton.Content = "Not secure";
            SecurityButton.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(205, 83, 83));
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
            SiteConnectionText.Foreground = (System.Windows.Media.Brush)FindResource("QuartzAccentBrush");
            SitePrivacyNoteText.Text = "The connection is encrypted. The site may still store cookies and other data in this browser profile.";
        }
        else if (uri?.Scheme == Uri.UriSchemeHttp)
        {
            SiteConnectionText.Text = "Not secure HTTP connection";
            SiteConnectionText.Foreground = System.Windows.Media.Brushes.DarkRed;
            SitePrivacyNoteText.Text = "This connection is not encrypted. Avoid entering sensitive information on this page.";
        }
        else
        {
            SiteConnectionText.Text = "Local or internal page";
            SiteConnectionText.Foreground = (System.Windows.Media.Brush)FindResource("QuartzMutedTextBrush");
            SitePrivacyNoteText.Text = "This page is provided by Quartz or WebView2 and does not use a normal website connection.";
        }
    }

    private Uri? GetCurrentPageUri()
    {
        var source = _activeTab?.Browser.CoreWebView2?.Source ?? _activeTab?.Browser.Source?.AbsoluteUri;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool IsCertificateError(CoreWebView2WebErrorStatus status) =>
        status is CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
            CoreWebView2WebErrorStatus.CertificateExpired or
            CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
            CoreWebView2WebErrorStatus.CertificateRevoked or
            CoreWebView2WebErrorStatus.CertificateIsInvalid;

    private string? GetActivePageUrl()
    {
        var source = _activeTab?.Browser.CoreWebView2?.Source;
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

        var core = tab.Browser.CoreWebView2;
        var url = core.Source;
        var title = core.DocumentTitle;

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
            SettingsButton.FontWeight = FontWeights.Normal;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        HistoryPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.FontWeight = willShow ? FontWeights.SemiBold : FontWeights.Normal;
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
            HistoryButton.FontWeight = FontWeights.Normal;
            SecurityButton.FontWeight = FontWeights.Normal;
            PopulateSettingsControls();
        }

        SettingsPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.FontWeight = willShow ? FontWeights.SemiBold : FontWeights.Normal;
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
            HistoryButton.FontWeight = FontWeights.Normal;
            SettingsButton.FontWeight = FontWeights.Normal;
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
        _isPopulatingSettings = false;
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
            HistoryButton.FontWeight = FontWeights.Normal;
            SettingsButton.FontWeight = FontWeights.Normal;
            SecurityButton.FontWeight = FontWeights.Normal;
        }

        PerformancePanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = willShow ? new GridLength(360) : new GridLength(0);
    }

    private void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var savePath = _downloadService.CreateSavePath(e.ResultFilePath, e.DownloadOperation.Uri);
            e.ResultFilePath = savePath;
            e.Handled = true;
            var item = _downloadService.Track(e.DownloadOperation, savePath);
            StatusText.Text = $"Downloading {item.FileName}...";
            ShowDownloadsWindow();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            e.Cancel = true;
            MessageBox.Show(
                $"Quartz could not start this download.\n\n{exception.Message}",
                "Quartz download error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HandlePermissionRequested(CoreWebView2PermissionRequestedEventArgs e)
    {
        var permissionName = GetPermissionName(e.PermissionKind);
        if (permissionName is null)
        {
            return;
        }

        e.Handled = true;
        e.SavesInProfile = false;
        e.State = CoreWebView2PermissionState.Deny;

        var origin = e.Uri ?? string.Empty;
        var domain = Uri.TryCreate(origin, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "This site";
        var prompt = new PermissionPromptWindow(domain, permissionName, origin) { Owner = this };
        if (prompt.ShowDialog() == true)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
    }

    private static string? GetPermissionName(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => "camera",
        CoreWebView2PermissionKind.Microphone => "microphone",
        CoreWebView2PermissionKind.Geolocation => "location",
        CoreWebView2PermissionKind.Notifications => "notifications",
        _ => null
    };

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
            var core = tab.Browser.CoreWebView2;
            var cookies = await core.CookieManager.GetCookiesAsync(uri.GetLeftPart(UriPartial.Authority));
            foreach (var cookie in cookies)
            {
                core.CookieManager.DeleteCookie(cookie);
            }

            await core.ExecuteScriptAsync(
                "(async()=>{localStorage.clear();sessionStorage.clear();" +
                "if(self.caches){for(const k of await caches.keys())await caches.delete(k);}" +
                "if(indexedDB.databases){for(const d of await indexedDB.databases())if(d.name)indexedDB.deleteDatabase(d.name);}" +
                "if(navigator.serviceWorker){for(const r of await navigator.serviceWorker.getRegistrations())await r.unregister();}})();");
            StatusText.Text = $"Site data cleared for {uri.Host}.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
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
        var core = _activeTab?.Browser.CoreWebView2;
        if (core is null)
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
            var kinds = CoreWebView2BrowsingDataKinds.Cookies |
                        CoreWebView2BrowsingDataKinds.AllDomStorage |
                        CoreWebView2BrowsingDataKinds.CacheStorage |
                        CoreWebView2BrowsingDataKinds.ServiceWorkers;
            await core.Profile.ClearBrowsingDataAsync(kinds);
            StatusText.Text = "Cookies and site data cleared.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
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
        var core = _activeTab?.Browser.CoreWebView2;
        if (core?.CanGoBack == true)
        {
            core.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var core = _activeTab?.Browser.CoreWebView2;
        if (core?.CanGoForward == true)
        {
            core.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) =>
        _activeTab?.Browser.CoreWebView2?.Reload();

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
        ShowDownloadsWindow();

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

        BookmarksBarBorder.Visibility = Visibility.Visible;
        StatusText.Text = _bookmarkService.Bookmarks.Count == 0
            ? "Bookmarks bar is open. Add a bookmark with Ctrl+D."
            : "Bookmarks bar is open.";
    }

    private void PerformanceButton_Click(object sender, RoutedEventArgs e) =>
        TogglePerformancePanel();

    private void NewPrivateWindowButton_Click(object sender, RoutedEventArgs e) =>
        OpenPrivateWindow();

    private static void OpenPrivateWindow() =>
        new MainWindow(isPrivate: true).Show();

    private void ShowDownloadsWindow()
    {
        if (_downloadsWindow is null)
        {
            _downloadsWindow = new DownloadsWindow(_downloadService, _isPrivate) { Owner = this };
            _downloadsWindow.Closed += (_, _) => _downloadsWindow = null;
        }

        _downloadsWindow.Show();
        _downloadsWindow.Activate();
    }

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

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem { Tag: BrowserTab tab } && tab != _activeTab)
        {
            SetActiveTab(tab);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CloseOpenPanel())
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
            ShowDownloadsWindow();
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
        _downloadsWindow?.Close();

        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
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
