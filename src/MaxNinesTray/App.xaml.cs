using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace MaxNines.Tray;

/// <summary>
/// Lives in the notification area, with no window until its icon is clicked, and keeps
/// running until Quit is chosen.
/// </summary>
public partial class App : Application
{
    Store? store;
    Tray? tray;
    Flyout? flyout;
    SettingsWindow? settings;
    Mutex? single;

    public App()
    {
        InitializeComponent();
        // Closing its windows doesn't end it: only Quit does.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One copy at a time, so there's one icon and one set of notifications.
        single = new Mutex(true, @"Local\MaxNines.Tray", out var first);
        if (!first)
        {
            Exit();
            return;
        }
        store = new Store();
        flyout = new Flyout(store, OpenSettings, Quit);
        tray = new Tray(store, () => flyout.Toggle(), OpenSettings, Quit);
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        store.Notifier.Start(
            activated.Kind == ExtendedActivationKind.AppNotification
                ? activated.Data as AppNotificationActivatedEventArgs
                : null);
        if (!store.IsSetUp)
        {
            OpenSettings();
        }
    }

    void OpenSettings()
    {
        settings ??= new SettingsWindow(store!);
        settings.Open();
    }

    void Quit()
    {
        store?.Notifier.Stop();
        tray?.Dispose();
        single?.ReleaseMutex();
        Exit();
    }
}
