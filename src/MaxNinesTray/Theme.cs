using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace MaxNines.Tray;

/// <summary>The colours of MaxNines' own Carbon theme, so the app looks like the site.</summary>
static class Theme
{
    public static readonly SolidColorBrush Surface = Brush(0x0F1114);
    public static readonly SolidColorBrush Sunken = Brush(0x0A0B0E);
    public static readonly SolidColorBrush Raised = Brush(0x1B1E24);
    public static readonly SolidColorBrush Ink = Brush(0xF3F5F4);
    public static readonly SolidColorBrush Muted = Brush(0x9EA5AB);
    public static readonly SolidColorBrush Edge = Brush(0x1E2227);
    public static readonly SolidColorBrush EdgeStrong = Brush(0x30353C);
    public static readonly SolidColorBrush Accent = Brush(0x6FDC8C);
    public static readonly SolidColorBrush AccentText = Brush(0x06200F);
    public static readonly SolidColorBrush AccentInk = Brush(0x8CE8A4);
    public static readonly SolidColorBrush Good = Brush(0x6FDC8C);
    public static readonly SolidColorBrush GoodSurface = Brush(0x102A1A);
    public static readonly SolidColorBrush Bad = Brush(0xFF7B7D);
    public static readonly SolidColorBrush Warn = Brush(0xF0B64E);

    /// <summary>For small capitals over a section and details, as the site has them.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    static SolidColorBrush Brush(uint rgb) =>
        new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    public static TextBlock Words(
        string text, double size = 13, SolidColorBrush? brush = null, FontWeight? weight = null, bool mono = false) =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = brush ?? Ink,
            FontWeight = weight ?? FontWeights.Normal,
            FontFamily = mono ? Mono : FontFamily.XamlAutoFontFamily,
            TextWrapping = TextWrapping.Wrap,
        };

    /// <summary>Small capitals over a section.</summary>
    public static TextBlock Label(string text) =>
        Words(text, 10, Muted, FontWeights.Medium, mono: true);

    /// <summary>A line of detail under something, such as how long ago it started.</summary>
    public static TextBlock Detail(string text) => Words(text, 11, Muted, mono: true);

    /// <summary>The quiet bordered button used for what can be done to an alert.</summary>
    public static Button Quiet(string text, Action click, bool filled = true)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            Background = filled ? Raised : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = Ink,
            BorderBrush = EdgeStrong,
            BorderThickness = new Thickness(1),
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The style of the button that does the main thing, such as signing in.</summary>
    public static Style AccentButton() => (Style)Application.Current.Resources["AccentButtonStyle"];

    /// <summary>A button that looks like a link.</summary>
    public static HyperlinkButton Link(string text, Action click, SolidColorBrush? brush = null)
    {
        var link = new HyperlinkButton
        {
            Content = text,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = brush ?? AccentInk,
            Padding = new Thickness(9, 7, 9, 7),
        };
        link.Click += (_, _) => click();
        return link;
    }

    public static Border Line() => new() { Height = 1, Background = Edge };
}
