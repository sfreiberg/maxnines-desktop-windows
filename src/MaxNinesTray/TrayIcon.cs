using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Input;
using H.NotifyIcon;
using MaxNines.Kit;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace MaxNines.Tray;

/// <summary>What the icon says: the worst thing happening.</summary>
enum IconState
{
    /// <summary>Nothing firing: plain, like the icons around it.</summary>
    Quiet,

    /// <summary>Warnings only: an amber square.</summary>
    Warnings,

    /// <summary>Something critical: a red dot.</summary>
    Critical,

    /// <summary>
    /// The server can't be asked, or hasn't answered yet: dimmed and dashed, so quiet
    /// isn't mistaken for fine.
    /// </summary>
    Unknown,
}

/// <summary>The icon in the notification area, and its menu.</summary>
sealed class Tray : IDisposable
{
    readonly Store store;
    readonly TaskbarIcon icon;
    Icon? shown;
    (IconState, bool, int)? drawn;

    public Tray(Store store, Action openFlyout, Action openSettings, Action quit)
    {
        this.store = store;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open MaxNines", Command = new Command(() => store.Open("/")) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Settings", Command = new Command(openSettings) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit MaxNines", Command = new Command(quit) });
        icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            LeftClickCommand = new Command(openFlyout),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
        };
        Update();
        icon.ForceCreate();
        store.Changed += Update;
    }

    /// <summary>Redraws the icon if what it says has changed, and keeps its tooltip current.</summary>
    public void Update()
    {
        var state = store.Overview is null || store.Problem is not null
            ? IconState.Unknown
            : store.Overview.Worst switch
            {
                Severity.Critical => IconState.Critical,
                Severity.Warning => IconState.Warnings,
                _ => IconState.Quiet,
            };
        // The taskbar can be light or dark, and the icon is drawn to suit it.
        var key = (state, TaskbarIsLight(), IconSize());
        if (drawn != key)
        {
            var next = Draw(state, key.Item2, key.Item3);
            icon.Icon = next;
            shown?.Dispose();
            shown = next;
            drawn = key;
        }
        icon.ToolTipText = $"MaxNines: {store.Headline}";
    }

    public void Dispose()
    {
        store.Changed -= Update;
        icon.Dispose();
        shown?.Dispose();
    }

    static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    /// <summary>A small icon's size at the screen's scale, such as 32 pixels at 200%.</summary>
    static int IconSize() => (int)Math.Round(16 * GetDpiForSystem() / 96.0);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    /// <summary>MaxNines' mark, a 9 in a rounded square, with the state in its corner.</summary>
    static Icon Draw(IconState state, bool lightTaskbar, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            float scale = size / 16f;
            var ink = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.FromArgb(243, 245, 244);
            if (state == IconState.Unknown)
            {
                ink = Color.FromArgb(160, ink);
            }
            using var pen = new Pen(ink, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (state == IconState.Unknown)
            {
                pen.DashPattern = [2.4f / 1.4f, 2f / 1.4f];
            }
            using (var square = RoundedSquare(1 * scale, 1 * scale, 14 * scale, 4 * scale))
            {
                graphics.DrawPath(pen, square);
            }
            pen.DashStyle = DashStyle.Solid;
            using (var nine = new GraphicsPath())
            {
                nine.AddEllipse(5.6f * scale, 4.2f * scale, 4.8f * scale, 4.8f * scale);
                nine.StartFigure();
                nine.AddLine(10.4f * scale, 6.6f * scale, 10.4f * scale, 8.8f * scale);
                nine.AddBezier(
                    10.4f * scale, 8.8f * scale, 10.4f * scale, 10.6f * scale,
                    9.3f * scale, 11.8f * scale, 7.5f * scale, 12f * scale);
                graphics.DrawPath(pen, nine);
            }

            // Told apart by shape as well as colour, with a ring the colour of the
            // taskbar so it stands clear of the mark.
            var mark = new RectangleF(9.5f * scale, 9.5f * scale, 6.5f * scale, 6.5f * scale);
            var ring = RectangleF.Inflate(mark, 1.2f * scale, 1.2f * scale);
            var behind = lightTaskbar ? Color.FromArgb(238, 238, 238) : Color.FromArgb(32, 32, 32);
            if (state == IconState.Critical)
            {
                using var clear = new SolidBrush(behind);
                graphics.FillEllipse(clear, ring);
                using var red = new SolidBrush(Color.FromArgb(232, 64, 68));
                graphics.FillEllipse(red, mark);
            }
            else if (state == IconState.Warnings)
            {
                using var clear = new SolidBrush(behind);
                using var ringPath = RoundedSquare(ring.X, ring.Y, ring.Width, 2.4f * scale);
                graphics.FillPath(clear, ringPath);
                using var amber = new SolidBrush(Color.FromArgb(240, 168, 40));
                using var markPath = RoundedSquare(mark.X, mark.Y, mark.Width, 1.6f * scale);
                graphics.FillPath(amber, markPath);
            }
        }
        var handle = bitmap.GetHicon();
        try
        {
            // A copy that owns its handle, so the one made here can go at once.
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedSquare(float x, float y, float side, float radius)
    {
        var path = new GraphicsPath();
        var corner = radius * 2;
        path.AddArc(x, y, corner, corner, 180, 90);
        path.AddArc(x + side - corner, y, corner, corner, 270, 90);
        path.AddArc(x + side - corner, y + side - corner, corner, corner, 0, 90);
        path.AddArc(x, y + side - corner, corner, corner, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>A command that just runs something.</summary>
sealed class Command(Action run) : ICommand
{
#pragma warning disable CS0067 // It never changes whether it can run.
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => run();
}
