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
    readonly PasswordBox apiKey = new() { Header = "API key" };
    readonly ComboBox interval = new() { Header = "Check every", MinWidth = 160 };
    readonly TextBlock clear = Theme.Words("", 12, Theme.Warn);
    readonly TextBlock tested = Theme.Words("", 12);
    readonly Button test = new() { Content = "Test connection" };
    readonly Button save = new() { Content = "Save", Style = AccentStyle() };
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
        apiKey.PasswordChanged += (_, _) => Check();
        test.Click += async (_, _) => await Test();
        save.Click += (_, _) => Save();
        foreach (var box in new[] { critical, warnings, resolved })
        {
            box.Click += (_, _) => SaveNotifications();
        }
        openAtLogin.Click += (_, _) => ChangeOpenAtLogin();

        var server = new StackPanel { Spacing = 12 };
        server.Children.Add(Theme.Label("SERVER"));
        server.Children.Add(address);
        server.Children.Add(apiKey);
        server.Children.Add(Theme.Words(
            "Kept in Windows Credential Manager. A viewer key shows alerts. A responder key can also acknowledge and silence them.",
            12, Theme.Muted));
        server.Children.Add(clear);
        var testing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        testing.Children.Add(test);
        testing.Children.Add(tested);
        tested.VerticalAlignment = VerticalAlignment.Center;
        server.Children.Add(testing);
        server.Children.Add(interval);
        save.HorizontalAlignment = HorizontalAlignment.Right;
        server.Children.Add(save);

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
        Content = new ScrollViewer { Content = page, Background = Theme.Surface };

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
        apiKey.Password = "";
        apiKey.PlaceholderText = store.HasApiKey ? "Leave empty to keep the one that's saved" : "From Settings in MaxNines";
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(Store.Intervals, settings.IntervalSecs));
        tested.Text = "";
        critical.IsChecked = settings.NotifyCritical;
        warnings.IsChecked = settings.NotifyWarnings;
        resolved.IsChecked = settings.NotifyResolved;
        openAtLogin.IsChecked = OpenAtLogin.IsOn;
        loginProblem.Text = "";
        notificationsProblem.Text = "";
        Check();
        AppWindow.Show();
        Activate();
    }

    /// <summary>What can be done with what's typed, and what to warn about.</summary>
    void Check()
    {
        var hasKey = apiKey.Password.Length > 0 || store.HasApiKey;
        var typed = address.Text.Trim();
        test.IsEnabled = typed.Length > 0 && hasKey;
        save.IsEnabled = typed.Length > 0 && hasKey;
        clear.Text = "";
        try
        {
            if (typed.Length > 0 && new Client(typed, "").SendsKeyInTheClear)
            {
                clear.Text = "Over plain http the key crosses the network unencrypted, so only use it on a network you trust.";
            }
        }
        catch (ClientException error)
        {
            clear.Text = error.Message;
            test.IsEnabled = false;
            save.IsEnabled = false;
        }
        notificationsOff.Visibility = !store.Notifier.Allowed && store.NotifyWhen.Any
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    async Task Test()
    {
        test.IsEnabled = false;
        tested.Text = "Trying…";
        tested.Foreground = Theme.Muted;
        var (worked, message) = await store.Test(address.Text, apiKey.Password);
        tested.Text = message;
        tested.Foreground = worked ? Theme.Good : Theme.Bad;
        Check();
    }

    void Save()
    {
        if (store.Save(address.Text, apiKey.Password, Store.Intervals[Math.Max(0, interval.SelectedIndex)]) is { } problem)
        {
            tested.Text = problem;
            tested.Foreground = Theme.Bad;
            return;
        }
        apiKey.Password = "";
        apiKey.PlaceholderText = "Leave empty to keep the one that's saved";
        tested.Text = "Saved.";
        tested.Foreground = Theme.Good;
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
