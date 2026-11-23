using Microsoft.UI.Dispatching;
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

    public App()
    {
        InitializeComponent();
        // Closing its windows doesn't end it: only Quit does.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    /// <summary>Only ever started once at a time: see <see cref="Program"/>.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        store = new Store();
        flyout = new Flyout(store, OpenSettings, Quit);
        tray = new Tray(store, () => flyout.Toggle(), OpenSettings, Quit);
        var current = AppInstance.GetCurrent();
        // Another copy hands over on another thread, so it's handled on this one.
        var queue = DispatcherQueue.GetForCurrentThread();
        current.Activated += (_, again) => queue.TryEnqueue(() => StartedAgain(again));
        var activated = current.GetActivatedEventArgs();
        store.Notifier.Start(
            activated.Kind == ExtendedActivationKind.AppNotification
                ? activated.Data as AppNotificationActivatedEventArgs
                : null);
        if (!store.IsSetUp)
        {
            OpenSettings();
        }
    }

    /// <summary>
    /// Started again while it's running, which <see cref="Program"/> hands on to this
    /// copy. From a notification, it does what that says; otherwise its settings open,
    /// so starting it, such as from the Start menu, is never seen to do nothing.
    /// </summary>
    void StartedAgain(AppActivationArguments again)
    {
        if (again.Data is AppNotificationActivatedEventArgs notification)
        {
            store!.Notifier.Handle(notification);
        }
        else
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
        Exit();
    }
}
