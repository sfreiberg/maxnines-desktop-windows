using System.Runtime.InteropServices;
using MaxNines.Kit;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using FontWeights = Microsoft.UI.Text.FontWeights;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace MaxNines.Tray;

/// <summary>What opens over the notification area when the icon is clicked.</summary>
sealed class Flyout : Window
{
    readonly Store store;
    readonly Action openSettings;
    readonly Action quit;
    readonly StackPanel body = new();
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(5) };
    bool visible;
    DateTimeOffset hiddenAt;
    PointInt32 anchor;

    const int WidthDip = 400;
    const int MostHeightDip = 720;
    const int MostSilences = 3;

    public Flyout(Store store, Action openSettings, Action quit)
    {
        this.store = store;
        this.openSettings = openSettings;
        this.quit = quit;
        Title = "MaxNines";

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, closing) =>
        {
            closing.Cancel = true;
            Hide();
        };
        // Like a menu, it goes away when something else is clicked.
        Activated += (_, activated) =>
        {
            if (activated.WindowActivationState == WindowActivationState.Deactivated)
            {
                Hide();
            }
        };

        Content = new Grid
        {
            Background = Theme.Surface,
            Children = { new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } },
        };
        body.SizeChanged += (_, _) => Fit();
        store.Changed += () =>
        {
            if (visible)
            {
                Build();
            }
        };
        // Redrawn now and then, so ages and "updated" keep up between checks.
        ticker.Tick += (_, _) => Build();
    }

    /// <summary>Opens it, or closes it if it's open: clicking the icon again closes it.</summary>
    public void Toggle()
    {
        // Clicking the icon takes focus away first, which has already closed it.
        if (visible || DateTimeOffset.UtcNow - hiddenAt < TimeSpan.FromMilliseconds(300))
        {
            Hide();
            return;
        }
        GetCursorPos(out var cursor);
        anchor = new PointInt32(cursor.X, cursor.Y);
        Build();
        visible = true;
        Fit();
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        ticker.Start();
    }

    void Hide()
    {
        if (!visible)
        {
            return;
        }
        visible = false;
        hiddenAt = DateTimeOffset.UtcNow;
        ticker.Stop();
        AppWindow.Hide();
    }

    /// <summary>
    /// Sizes it to what it shows, up to a limit past which it scrolls, and puts it by the
    /// notification area, wherever the taskbar is.
    /// </summary>
    void Fit()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(Math.Min(Math.Max(body.ActualHeight, 120), MostHeightDip) * scale);
        var work = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
        var gap = (int)Math.Round(12 * scale);
        var x = Math.Clamp(anchor.X - width / 2, work.X + gap, work.X + work.Width - width - gap);
        // Below the cursor when the taskbar is at the top, above it otherwise.
        var y = anchor.Y < work.Y + work.Height / 2 ? work.Y + gap : work.Y + work.Height - height - gap;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    void Build()
    {
        var now = DateTimeOffset.UtcNow;
        var children = body.Children;
        children.Clear();
        children.Add(Header(now));
        children.Add(Theme.Line());
        if (!store.IsSetUp)
        {
            children.Add(NotSetUp());
        }
        else if (store.Overview is { } overview)
        {
            children.Add(Counts(overview));
            children.Add(Theme.Line());
            Alerts(overview, now);
            Silences(overview, now);
        }
        else
        {
            children.Add(Padded(
                Theme.Words(
                    store.Problem is null ? "Asking the server…" : "Nothing to show until the server answers.",
                    13, Theme.Muted),
                24));
        }
        if (store.ActionProblem is { } problem)
        {
            children.Add(Notice(problem));
        }
        children.Add(Theme.Line());
        children.Add(Footer());
    }

    UIElement Header(DateTimeOffset now)
    {
        var grid = new Grid { Padding = new Thickness(16, 14, 16, 14), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Theme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "9",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.AccentText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(mark);

        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Theme.Words(store.Headline, 14, weight: FontWeights.SemiBold));
        words.Children.Add(Theme.Words(Status(now), 11, store.Problem is null ? Theme.Muted : Theme.Warn, mono: true));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var refresh = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Theme.Raised,
            BorderBrush = Theme.EdgeStrong,
            IsEnabled = store.IsSetUp,
            Content = new FontIcon { Glyph = "", FontSize = 13 },
        };
        AutomationName(refresh, "Refresh");
        refresh.Click += async (_, _) => await store.Refresh();
        Grid.SetColumn(refresh, 2);
        grid.Children.Add(refresh);
        return grid;
    }

    /// <summary>Where it's looking, and how fresh what it shows is.</summary>
    string Status(DateTimeOffset now)
    {
        if (!store.IsSetUp)
        {
            return store.Problem ?? "Add your server in Settings";
        }
        var server = Uri.TryCreate(store.Settings.Address, UriKind.Absolute, out var url)
            ? url.Authority
            : store.Settings.Address;
        if (store.Problem is { } problem)
        {
            var since = store.Updated is { } heard ? $", last heard {Times.Age(heard.ToUnixTimeMilliseconds(), now)} ago" : "";
            return problem + since;
        }
        return store.Updated is { } updated
            ? $"{server} · updated {Times.Age(updated.ToUnixTimeMilliseconds(), now)} ago"
            : server;
    }

    UIElement Counts(Overview overview)
    {
        var tiles = new List<UIElement>
        {
            Count("HOSTS ONLINE", $"{overview.HostsOnline}", overview.Hosts, "/hosts", Theme.Ink),
            Count(
                "FIRING", $"{overview.Firing.Count}", null, "/alerts",
                overview.Worst switch
                {
                    null => Theme.Good,
                    Severity.Critical => Theme.Bad,
                    _ => Theme.Warn,
                }),
        };
        if (overview.Checks is { } checks)
        {
            // Only checks that have run can pass or fail, so until any has since the
            // server started, there's nothing to count.
            tiles.Add(checks.Run == 0
                ? Count("CHECKS PASSING", "–", null, "/checks", Theme.Muted)
                : Count(
                    "CHECKS PASSING", $"{checks.Passing}", checks.Run, "/checks",
                    checks.Failing > 0 ? Theme.Bad : Theme.Ink));
        }
        var grid = new Grid { ColumnSpacing = 1, Background = Theme.Edge };
        for (var column = 0; column < tiles.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn((FrameworkElement)tiles[column], column);
            grid.Children.Add(tiles[column]);
        }
        return grid;
    }

    Button Count(string label, string value, int? whole, string path, SolidColorBrush colour)
    {
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(Theme.Label(label));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        line.Children.Add(Theme.Words(value, 18, colour, FontWeights.SemiBold));
        if (whole is { } of)
        {
            var rest = Theme.Words($"of {of}", 12, Theme.Muted);
            rest.VerticalAlignment = VerticalAlignment.Bottom;
            rest.Margin = new Thickness(0, 0, 0, 3);
            line.Children.Add(rest);
        }
        words.Children.Add(line);
        var button = Clickable(words, () => store.Open(path));
        button.Background = Theme.Surface;
        button.Padding = new Thickness(16, 10, 16, 10);
        return button;
    }

    void Alerts(Overview overview, DateTimeOffset now)
    {
        var children = body.Children;
        if (overview.Firing.Count == 0)
        {
            var clear = new StackPanel { Spacing = 10, Padding = new Thickness(0, 24, 0, 24) };
            clear.Children.Add(new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = Theme.GoodSurface,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new FontIcon { Glyph = "", FontSize = 18, Foreground = Theme.Good },
            });
            var words = Theme.Words("No alerts are firing", 14, weight: FontWeights.Medium);
            words.HorizontalAlignment = HorizontalAlignment.Center;
            clear.Children.Add(words);
            children.Add(clear);
        }
        else
        {
            children.Add(Section("FIRING NOW"));
            foreach (var alert in overview.Firing)
            {
                children.Add(FiringRow(alert, now));
                children.Add(Theme.Line());
            }
        }
        if (overview.Resolved.Count > 0)
        {
            children.Add(Theme.Line());
            children.Add(Section("RESOLVED IN THE LAST HOUR"));
            foreach (var alert in overview.Resolved)
            {
                children.Add(ResolvedRow(alert, now));
            }
        }
    }

    UIElement FiringRow(ServerAlert alert, DateTimeOffset now)
    {
        // Told apart by shape as well as colour.
        Shapes.Shape shape = alert.Severity == Severity.Critical
            ? new Shapes.Ellipse { Fill = Theme.Bad }
            : new Shapes.Rectangle { Fill = Theme.Warn, RadiusX = 2, RadiusY = 2 };
        shape.Width = 9;
        shape.Height = 9;
        shape.Margin = new Thickness(0, 5, 0, 0);
        shape.VerticalAlignment = VerticalAlignment.Top;
        AutomationName(shape, alert.Severity == Severity.Critical ? "Critical" : "Warning");

        var words = new StackPanel { Spacing = 4 };
        words.Children.Add(Clickable(
            Theme.Words(alert.Summary, 13, weight: FontWeights.Medium), () => store.Open(Notices.Page(alert))));
        var detail = new List<string> { alert.RuleName, Times.Age(alert.StartedMs, now) };
        if (alert.AcknowledgedBy is { } who)
        {
            detail.Add($"acknowledged by {who}");
        }
        if (alert.IsSilenced)
        {
            detail.Add("silenced");
        }
        words.Children.Add(Theme.Detail(string.Join(" · ", detail)));
        if (!alert.IsAcknowledged || !alert.IsSilenced)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            if (!alert.IsAcknowledged)
            {
                actions.Children.Add(Theme.Quiet("Acknowledge", () => store.Acknowledge(alert)));
            }
            if (!alert.IsSilenced)
            {
                actions.Children.Add(Theme.Quiet("Silence 1 hour", () => store.SilenceForAnHour(alert), filled: false));
            }
            words.Children.Add(actions);
        }
        return Row(shape, words, new Thickness(16, 10, 16, 10));
    }

    UIElement ResolvedRow(ServerAlert alert, DateTimeOffset now)
    {
        var tick = new FontIcon { Glyph = "", FontSize = 10, Foreground = Theme.Good, Margin = new Thickness(0, 4, 0, 0) };
        AutomationName(tick, "Resolved");
        var words = new StackPanel { Spacing = 3 };
        words.Children.Add(Clickable(Theme.Words(alert.Summary, 13, Theme.Muted), () => store.Open(Notices.Page(alert))));
        words.Children.Add(Theme.Detail($"{alert.RuleName} · {Times.Age(alert.ResolvedMs ?? alert.StartedMs, now)} ago"));
        return Row(tick, words, new Thickness(16, 8, 16, 8));
    }

    void Silences(Overview overview, DateTimeOffset now)
    {
        if (overview.Silences.Count == 0)
        {
            return;
        }
        var children = body.Children;
        children.Add(Theme.Line());
        children.Add(Section("SILENCED"));
        foreach (var silence in overview.Silences.Take(MostSilences))
        {
            var bell = new FontIcon { Glyph = "", FontSize = 11, Foreground = Theme.Muted, Margin = new Thickness(0, 3, 0, 0) };
            AutomationName(bell, "Silenced");
            var words = new StackPanel { Spacing = 3 };
            words.Children.Add(Clickable(Theme.Words(silence.Reason, 13), () => store.Open("/alerts/silences")));
            words.Children.Add(Theme.Detail($"{silence.Covers} · ends in {Times.Left(silence.EndsMs, now)}"));
            children.Add(Row(bell, words, new Thickness(16, 8, 16, 8)));
        }
        var more = overview.Silences.Count - MostSilences;
        if (more > 0)
        {
            var link = Theme.Link($"{more} more", () => store.Open("/alerts/silences"));
            link.Margin = new Thickness(7, 0, 0, 10);
            children.Add(link);
        }
    }

    UIElement NotSetUp()
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        var words = Theme.Words(
            "Enter your MaxNines server's address and an API key to see its alerts here.", 13, Theme.Muted);
        words.TextAlignment = TextAlignment.Center;
        panel.Children.Add(words);
        var open = Theme.Quiet("Open Settings", openSettings);
        open.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(open);
        return panel;
    }

    UIElement Notice(string problem)
    {
        var grid = new Grid { Padding = new Thickness(16, 10, 16, 10), ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = Theme.Warn, VerticalAlignment = VerticalAlignment.Top });
        var words = Theme.Words(problem, 12);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var dismiss = Theme.Quiet("Dismiss", () =>
        {
            store.ActionProblem = null;
            Build();
        }, filled: false);
        Grid.SetColumn(dismiss, 2);
        grid.Children.Add(dismiss);
        return grid;
    }

    UIElement Footer()
    {
        var grid = new Grid { Padding = new Thickness(7, 6, 8, 6), Background = Theme.Sunken };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, path) in new[] { ("Overview", "/"), ("Alerts", "/alerts"), ("Hosts", "/hosts"), ("Metrics", "/metrics") })
        {
            var link = Theme.Link(title, () => store.Open(path), store.IsSetUp ? Theme.AccentInk : Theme.Muted);
            link.IsEnabled = store.IsSetUp;
            links.Children.Add(link);
        }
        grid.Children.Add(links);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        tools.Children.Add(IconButton("", "Settings", openSettings));
        tools.Children.Add(IconButton("", "Quit MaxNines", quit));
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);
        return grid;
    }

    static Button IconButton(string glyph, string name, Action click)
    {
        var button = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Theme.Muted },
        };
        AutomationName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    static UIElement Section(string title)
    {
        var label = Theme.Label(title);
        label.Margin = new Thickness(16, 12, 16, 4);
        return label;
    }

    static UIElement Padded(FrameworkElement element, double padding)
    {
        element.Margin = new Thickness(padding);
        element.HorizontalAlignment = HorizontalAlignment.Center;
        return element;
    }

    /// <summary>A mark beside some words, as each row of alerts and silences has.</summary>
    static Grid Row(FrameworkElement mark, FrameworkElement words, Thickness padding)
    {
        var grid = new Grid { Padding = padding, ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mark.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(mark);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return grid;
    }

    /// <summary>Something to click that keeps its own look.</summary>
    static Button Clickable(UIElement content, Action click)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        button.Click += (_, _) => click();
        return button;
    }

    static void AutomationName(UIElement element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
}
