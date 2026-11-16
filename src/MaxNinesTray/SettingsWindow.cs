using System.Security;
using MaxNines.Kit;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace MaxNines.Tray;

sealed class SettingsWindow : Window
{
    readonly Store store;
    readonly TextBox address = new() { Header = "Address", PlaceholderText = "https://monitor.example.com" };
    readonly ComboBox interval = new() { Header = "Check every", MinWidth = 160 };
    readonly TextBlock clear = Theme.Words("", 12, Theme.Warn);
    readonly Button signIn = new() { Content = "Sign in with browser", Style = AccentStyle() };
    readonly TextBlock signInHint = Theme.Words(
        "Opens your browser to allow this app on the server. A viewer sees alerts. A responder can also acknowledge and silence them.",
        12, Theme.Muted);
    readonly StackPanel waiting = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    readonly Button cancel = new() { Content = "Cancel" };
    readonly StackPanel signedIn = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    readonly TextBlock signedInAs = Theme.Words("", 13);
    readonly Button signOut = new() { Content = "Sign out" };
    readonly TextBlock outcome = Theme.Words("", 12, Theme.Bad);
    readonly CheckBox critical = new() { Content = "When a critical alert starts" };
    readonly CheckBox warnings = new() { Content = "When a warning starts" };
    readonly CheckBox resolved = new() { Content = "When an alert resolves" };
    readonly TextBlock notificationsOff = Theme.Words(
        "Windows has notifications from MaxNines turned off. Turn them on in Settings, System, Notifications.",
        12, Theme.Warn);
    readonly CheckBox openAtLogin = new() { Content = "Open at login" };
    readonly TextBlock loginProblem = Theme.Words("", 12, Theme.Bad);
    readonly TextBlock notificationsProblem = Theme.Words("", 12, Theme.Bad);

    public SettingsWindow(Store store)
    {
        this.store = store;
        Title = "MaxNines Settings";
        AppWindow.Resize(new SizeInt32(560, 760));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        foreach (var seconds in Store.Intervals)
        {
            interval.Items.Add(seconds < 60 ? $"{seconds} seconds" : "1 minute");
        }
        address.TextChanged += (_, _) => Check();
        interval.SelectionChanged += (_, _) => SaveInterval();
        signIn.Click += async (_, _) => await SignIn();
        cancel.Click += (_, _) => store.CancelSignIn();
        signOut.Click += async (_, _) => await SignOut();
        foreach (var box in new[] { critical, warnings, resolved })
        {
            box.Click += (_, _) => SaveNotifications();
        }
        openAtLogin.Click += (_, _) => ChangeOpenAtLogin();
        // Signing in finishes, or the server signs the app out, while it's open.
        store.Changed += ShowSignIn;

        var waitingWords = Theme.Words("Waiting for you to allow it in the browser…", 13, Theme.Muted);
        waitingWords.VerticalAlignment = VerticalAlignment.Center;
        waiting.Children.Add(waitingWords);
        waiting.Children.Add(cancel);
        signedInAs.VerticalAlignment = VerticalAlignment.Center;
        signedIn.Children.Add(signedInAs);
        signedIn.Children.Add(signOut);

        var server = new StackPanel { Spacing = 12 };
        server.Children.Add(Theme.Label("SERVER"));
        server.Children.Add(address);
        server.Children.Add(clear);
        server.Children.Add(signIn);
        server.Children.Add(signInHint);
        server.Children.Add(waiting);
        server.Children.Add(signedIn);
        server.Children.Add(outcome);
        server.Children.Add(interval);

        var notifications = new StackPanel { Spacing = 6 };
        notifications.Children.Add(Theme.Label("NOTIFICATIONS"));
        notifications.Children.Add(critical);
        notifications.Children.Add(warnings);
        notifications.Children.Add(resolved);
        notifications.Children.Add(Theme.Words("Silenced alerts never send one.", 12, Theme.Muted));
        notifications.Children.Add(notificationsOff);
        notifications.Children.Add(notificationsProblem);

        var login = new StackPanel { Spacing = 6 };
        login.Children.Add(openAtLogin);
        login.Children.Add(loginProblem);

        var page = new StackPanel { Spacing = 20, Padding = new Thickness(24) };
        page.Children.Add(server);
        page.Children.Add(Theme.Line());
        page.Children.Add(notifications);
        page.Children.Add(Theme.Line());
        page.Children.Add(login);
        page.Children.Add(Theme.Line());
        var build = Theme.Words(Build.Describe(), 12, Theme.Muted);
        build.IsTextSelectionEnabled = true;
        page.Children.Add(build);
        // Dark whatever Windows is set to, as the app draws its own dark background: the
        // controls would otherwise follow a light Windows and draw dark text on it.
        Content = new ScrollViewer { Content = page, Background = Theme.Surface, RequestedTheme = ElementTheme.Dark };
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;

        // Closing only hides it, so it opens again as it was left.
        AppWindow.Closing += (_, closing) =>
        {
            closing.Cancel = true;
            AppWindow.Hide();
        };
    }

    /// <summary>Opens it showing what's saved now.</summary>
    public void Open()
    {
        var settings = store.Settings;
        address.Text = settings.Address;
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(Store.Intervals, settings.IntervalSecs));
        outcome.Text = "";
        critical.IsChecked = settings.NotifyCritical;
        warnings.IsChecked = settings.NotifyWarnings;
        resolved.IsChecked = settings.NotifyResolved;
        openAtLogin.IsChecked = OpenAtLogin.IsOn;
        loginProblem.Text = "";
        notificationsProblem.Text = "";
        ShowSignIn();
        AppWindow.Show();
        Activate();
    }

    /// <summary>Whether it's signed in, signing in, or neither, and what can be done.</summary>
    void ShowSignIn()
    {
        var who = store.SignedInAs;
        var signingIn = store.IsSigningIn;
        signedInAs.Text = who is { Length: > 0 } ? $"Signed in as {who}" : "Signed in";
        signedIn.Visibility = Shown(who is not null);
        waiting.Visibility = Shown(signingIn);
        signIn.Visibility = Shown(who is null && !signingIn);
        signInHint.Visibility = signIn.Visibility;
        // Its tokens are for one server: signing out comes first.
        address.IsEnabled = who is null && !signingIn;
        // Such as the server having signed the app out.
        if (who is null && !signingIn && outcome.Text.Length == 0 && store.Problem is { } problem)
        {
            outcome.Text = problem;
        }
        Check();
    }

    static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What can be done with what's typed, and what to warn about.</summary>
    void Check()
    {
        var typed = address.Text.Trim();
        signIn.IsEnabled = typed.Length > 0;
        clear.Text = "";
        try
        {
            if (typed.Length > 0 && Client.SendsTokensInTheClear(Client.SiteRoot(typed)))
            {
                clear.Text = "Over plain http, signing in crosses the network unencrypted, so only use it on a network you trust.";
            }
        }
        catch (ClientException error)
        {
            clear.Text = error.Message;
            signIn.IsEnabled = false;
        }
        notificationsOff.Visibility = Shown(!store.Notifier.Allowed && store.NotifyWhen.Any);
    }

    async Task SignIn()
    {
        outcome.Text = "";
        outcome.Text = await store.SignIn(address.Text) ?? "";
        ShowSignIn();
    }

    async Task SignOut()
    {
        outcome.Text = "";
        outcome.Text = await store.SignOut() ?? "";
        ShowSignIn();
    }

    void SaveInterval()
    {
        var seconds = Store.Intervals[Math.Max(0, interval.SelectedIndex)];
        if (seconds == store.Settings.IntervalSecs)
        {
            return;
        }
        if (store.SaveInterval(seconds) is { } problem)
        {
            outcome.Text = problem;
            interval.SelectedIndex = Math.Max(0, Array.IndexOf(Store.Intervals, store.Settings.IntervalSecs));
        }
    }

    void SaveNotifications()
    {
        var problem = store.SaveNotifications(new NotifyWhen(
            critical.IsChecked == true, warnings.IsChecked == true, resolved.IsChecked == true));
        notificationsProblem.Text = problem ?? "";
        if (problem is not null)
        {
            // Show what's still saved.
            critical.IsChecked = store.Settings.NotifyCritical;
            warnings.IsChecked = store.Settings.NotifyWarnings;
            resolved.IsChecked = store.Settings.NotifyResolved;
        }
        Check();
    }

    void ChangeOpenAtLogin()
    {
        try
        {
            OpenAtLogin.Set(openAtLogin.IsChecked == true);
            loginProblem.Text = "";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            loginProblem.Text = $"Windows wouldn't change it: {error.Message}";
        }
        openAtLogin.IsChecked = OpenAtLogin.IsOn;
    }

    static Style AccentStyle() => (Style)Application.Current.Resources["AccentButtonStyle"];
}
