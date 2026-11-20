using System.Runtime.InteropServices;
using System.Text.Json;
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

    /// <summary>
    /// Words that change with the time, such as ages, and how to say them now. Only
    /// these are changed between checks: rebuilding everything would lose focus, a
    /// screen reader's place, and a click that landed while it happened.
    /// </summary>
    readonly List<(TextBlock Words, Func<DateTimeOffset, string> Say)> timed = [];

    /// <summary>What was shown when it was last built, apart from the time.</summary>
    string? built;
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
            // Dark whatever Windows is set to, as its background is.
            RequestedTheme = ElementTheme.Dark,
            Background = Theme.Surface,
            Children = { new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } },
        };
        body.SizeChanged += (_, _) => Fit();
        store.Changed += () =>
        {
            if (visible)
            {
                Update();
            }
        };
        // Ages and "updated" keep up between checks.
        ticker.Tick += (_, _) => Tick();
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
    /// notification area, wherever the taskbar is. It's never bigger than the screen
    /// leaves room for, as on a small or much enlarged one, where it scrolls instead.
    /// </summary>
    void Fit()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
        var gap = (int)Math.Round(12 * scale);
        var width = Math.Min((int)Math.Round(WidthDip * scale), work.Width - 2 * gap);
        var height = Math.Min(
            (int)Math.Round(Math.Min(Math.Max(body.ActualHeight, 120), MostHeightDip) * scale),
            work.Height - 2 * gap);
        var x = Math.Clamp(anchor.X - width / 2, work.X + gap, work.X + work.Width - width - gap);
        // Below the cursor when the taskbar is at the top, above it otherwise.
        var y = anchor.Y < work.Y + work.Height / 2 ? work.Y + gap : work.Y + work.Height - height - gap;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>Rebuilds it only if what it shows has changed, and otherwise just the times.</summary>
    void Update()
    {
        if (Shown() == built)
        {
            Tick();
        }
        else
        {
            Build();
        }
    }

    /// <summary>Everything it shows apart from the time, to tell whether that's changed.</summary>
    string Shown() => JsonSerializer.Serialize(new
    {
        store.IsSetUp,
        store.IsSigningIn,
        store.Settings.Address,
        store.Headline,
        store.Problem,
        store.ActionProblem,
        store.Overview,
        signInProblem,
        another,
    });

    void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (words, say) in timed)
        {
            words.Text = say(now);
        }
    }

    /// <summary>Words that are kept up with the time.</summary>
    TextBlock Timed(TextBlock words, Func<DateTimeOffset, string> say)
    {
        timed.Add((words, say));
        return words;
    }

    void Build()
    {
        var now = DateTimeOffset.UtcNow;
        built = Shown();
        timed.Clear();
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
        words.Children.Add(Timed(
            Theme.Words(Status(now), 11, store.Problem is null ? Theme.Muted : Theme.Warn, mono: true), Status));
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
            return store.Problem ?? "Not signed in";
        }
        var server = Host(store.Settings.Address);
        if (store.Problem is { } problem)
        {
            var since = store.Updated is { } heard ? $", last heard {Times.Age(heard, now)} ago" : "";
            return problem + since;
        }
        return store.Updated is { } updated
            ? $"{server} · updated {Times.Age(updated, now)} ago"
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
        string Detail(DateTimeOffset at)
        {
            var detail = new List<string> { alert.RuleName, Times.Age(alert.BreachedAt, at) };
            if (alert.AcknowledgedBy is { } who)
            {
                detail.Add($"acknowledged by {who}");
            }
            if (alert.IsSilenced)
            {
                detail.Add("silenced");
            }
            return string.Join(" · ", detail);
        }
        words.Children.Add(Timed(Theme.Detail(Detail(now)), Detail));
        // A silence names the alert's rule, so one whose rule is gone can't have one.
        var canSilence = !alert.IsSilenced && alert.CanBeSilenced;
        if (!alert.IsAcknowledged || canSilence)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            if (!alert.IsAcknowledged)
            {
                actions.Children.Add(Theme.Quiet("Acknowledge", () => store.Acknowledge(alert)));
            }
            if (canSilence)
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
        words.Children.Add(Clickable(Theme.Words(alert.SaysNow, 13, Theme.Muted), () => store.Open(Notices.Page(alert))));
        string Detail(DateTimeOffset at) => $"{alert.RuleName} · {Times.Age(alert.ResolvedAt ?? alert.StartedAt, at)} ago";
        words.Children.Add(Timed(Theme.Detail(Detail(now)), Detail));
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
            string Detail(DateTimeOffset at) => $"{silence.Covers} · ends in {Times.Left(silence.EndsAt, at)}";
            words.Children.Add(Timed(Theme.Detail(Detail(now)), Detail));
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

    /// <summary>Why the last sign in from here didn't work, if it didn't.</summary>
    string? signInProblem;

    /// <summary>Signing in to a server other than the one saved.</summary>
    bool another;

    /// <summary>What's been typed as the server's address, kept if it's redrawn.</summary>
    string typed = "";

    /// <summary>
    /// Signing in, right here: a button that opens the browser, with a place for the
    /// server's address the first time, before there's one to sign in to.
    /// </summary>
    UIElement NotSetUp()
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        void Add(FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(element);
        }
        TextBlock Centered(string text, SolidColorBrush brush, double size = 13)
        {
            var words = Theme.Words(text, size, brush);
            words.TextAlignment = TextAlignment.Center;
            return words;
        }

        if (store.IsSigningIn)
        {
            Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
            Add(Centered("Waiting for you to allow it in the browser…", Theme.Muted));
            Add(Theme.Quiet("Cancel", store.CancelSignIn, filled: false));
            return panel;
        }

        Add(Centered("Sign in to your MaxNines server to see its alerts here.", Theme.Muted));
        var saved = store.Settings.Address;
        if (saved.Length == 0 || another)
        {
            var address = new TextBox
            {
                Text = typed,
                PlaceholderText = "https://monitor.example.com",
                MinWidth = 240,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(address, "Server address");
            var signIn = new Button
            {
                Content = "Sign in",
                Style = Theme.AccentButton(),
                IsEnabled = typed.Trim().Length > 0,
            };
            address.TextChanged += (_, _) =>
            {
                typed = address.Text;
                signIn.IsEnabled = typed.Trim().Length > 0;
            };
            address.KeyDown += (_, key) =>
            {
                if (key.Key == Windows.System.VirtualKey.Enter && typed.Trim().Length > 0)
                {
                    SignIn(typed.Trim());
                }
            };
            signIn.Click += (_, _) => SignIn(typed.Trim());
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(address);
            row.Children.Add(signIn);
            Add(row);
            if (another)
            {
                Add(Theme.Link($"Back to {Host(saved)}", () =>
                {
                    another = false;
                    Build();
                }));
            }
        }
        else
        {
            var signIn = new Button
            {
                Content = $"Sign in to {Host(saved)}",
                Style = Theme.AccentButton(),
            };
            signIn.Click += (_, _) => SignIn(saved);
            Add(signIn);
            Add(Theme.Link("Use another server", () =>
            {
                typed = "";
                another = true;
                Build();
            }));
        }
        if (signInProblem is { } problem)
        {
            Add(Centered(problem, Theme.Bad, 12));
        }
        return panel;
    }

    /// <summary>A server as people know it: its name or address, without the scheme.</summary>
    static string Host(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var url) ? url.Authority : address;

    /// <summary>Signs in through the browser. Started from a button, so nothing it does throws.</summary>
    async void SignIn(string address)
    {
        signInProblem = null;
        another = false;
        try
        {
            signInProblem = await store.SignIn(address);
        }
        catch (Exception error)
        {
            signInProblem = error.Message;
        }
        if (visible)
        {
            Build();
        }
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
