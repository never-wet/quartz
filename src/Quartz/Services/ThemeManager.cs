using System.Windows;
using System.Windows.Media;
using Quartz.Models;

namespace Quartz.Services;

internal sealed record ThemePalette(
    string Canvas,
    string Surface,
    string Raised,
    string Text,
    string MutedText,
    string Border,
    string Address,
    string TabStrip,
    string Hover,
    string Pressed,
    string Sidebar,
    string ProgressTrack,
    string Private);

internal static class ThemeManager
{
    private static readonly IReadOnlyDictionary<BrowserTheme, ThemePalette> Palettes =
        new Dictionary<BrowserTheme, ThemePalette>
        {
            [BrowserTheme.Light] = new(
                "#F5F4F8", "#FFFFFF", "#FAF9FC", "#25232B", "#6F6B78", "#DDD9E4",
                "#F7F6FA", "#EDEAF3", "#EDEAF3", "#E1DCEB", "#F0EDF6", "#E8E5ED", "#4D3A68"),
            [BrowserTheme.Dark] = new(
                "#1E1E24", "#29282F", "#242329", "#F3F1F7", "#AAA5B3", "#45424D",
                "#201F25", "#25242C", "#393640", "#484351", "#19181E", "#403D49", "#8D73B5"),
            [BrowserTheme.Midnight] = new(
                "#101522", "#171D2B", "#131927", "#EAF2FF", "#96A4BB", "#2A3851",
                "#0F1522", "#111827", "#202D42", "#2B3B56", "#0B1020", "#293750", "#6D5A91"),
            [BrowserTheme.Neon] = new(
                "#101416", "#191F21", "#141A1B", "#F1FFF8", "#99B2A6", "#31433A",
                "#0E1512", "#121A16", "#26352C", "#354B3E", "#0A100D", "#304239", "#475E52")
        };

    private static readonly IReadOnlyDictionary<AccentPreset, string> Accents =
        new Dictionary<AccentPreset, string>
        {
            [AccentPreset.Violet] = "#765CB2",
            [AccentPreset.Blue] = "#3987D7",
            [AccentPreset.Teal] = "#1E9A8A",
            [AccentPreset.Rose] = "#C25377",
            [AccentPreset.Lime] = "#79C94B"
        };

    public static event EventHandler? AppearanceChanged;

    public static BrowserTheme CurrentTheme { get; private set; } = BrowserTheme.Light;

    public static AccentPreset CurrentAccent { get; private set; } = AccentPreset.Violet;

    public static ThemePalette CurrentPalette => Palettes[CurrentTheme];

    public static string CurrentAccentHex => Accents[CurrentAccent];

    public static AccentPreset GetRecommendedAccent(BrowserTheme theme) => theme switch
    {
        BrowserTheme.Dark => AccentPreset.Teal,
        BrowserTheme.Midnight => AccentPreset.Blue,
        BrowserTheme.Neon => AccentPreset.Lime,
        _ => AccentPreset.Violet
    };

    public static void Apply(BrowserTheme theme, AccentPreset accent)
    {
        if (!Palettes.TryGetValue(theme, out var palette) || !Accents.TryGetValue(accent, out var accentHex))
        {
            return;
        }

        CurrentTheme = theme;
        CurrentAccent = accent;

        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            SetBrush(resources, "QuartzCanvasBrush", palette.Canvas);
            SetBrush(resources, "QuartzSurfaceBrush", palette.Surface);
            SetBrush(resources, "QuartzRaisedBrush", palette.Raised);
            SetBrush(resources, "QuartzTextBrush", palette.Text);
            SetBrush(resources, "QuartzMutedTextBrush", palette.MutedText);
            SetBrush(resources, "QuartzBorderBrush", palette.Border);
            SetBrush(resources, "QuartzAddressBrush", palette.Address);
            SetBrush(resources, "QuartzTabStripBrush", palette.TabStrip);
            SetBrush(resources, "QuartzHoverBrush", palette.Hover);
            SetBrush(resources, "QuartzPressedBrush", palette.Pressed);
            SetBrush(resources, "QuartzSidebarBrush", palette.Sidebar);
            SetBrush(resources, "QuartzProgressTrackBrush", palette.ProgressTrack);
            SetBrush(resources, "QuartzPrivateBrush", palette.Private);
            SetBrush(resources, "QuartzAccentBrush", accentHex);
            SetBrush(resources, "QuartzAccentHoverBrush", AdjustBrightness(accentHex, -0.14));
            SetBrush(resources, "QuartzAccentSoftBrush", Mix(accentHex, palette.Surface, 0.18));
        }

        AppearanceChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string value)
    {
        resources[key] = new SolidColorBrush(ParseColor(value));
    }

    private static string AdjustBrightness(string hex, double change)
    {
        var color = ParseColor(hex);
        byte Adjust(byte channel) => (byte)Math.Clamp(channel + (255 * change), 0, 255);
        return $"#{Adjust(color.R):X2}{Adjust(color.G):X2}{Adjust(color.B):X2}";
    }

    private static string Mix(string foreground, string background, double foregroundWeight)
    {
        var front = ParseColor(foreground);
        var back = ParseColor(background);
        byte Blend(byte a, byte b) => (byte)Math.Round((a * foregroundWeight) + (b * (1 - foregroundWeight)));
        return $"#{Blend(front.R, back.R):X2}{Blend(front.G, back.G):X2}{Blend(front.B, back.B):X2}";
    }

    private static Color ParseColor(string value) =>
        (Color)ColorConverter.ConvertFromString(value);
}
