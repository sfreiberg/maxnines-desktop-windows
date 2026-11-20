using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace MaxNines.Tray;

/// <summary>
/// Starts the app, or, when the installer runs it as it removes the app, undoes what
/// the app set up outside its folder.
/// </summary>
static class Program
{
    /// <summary>What the installer runs it with as it uninstalls it.</summary>
    const string Uninstalling = "--uninstall";

    /// <summary>What the copy that's running registers as, so another can find it.</summary>
    const string RunningKey = "MaxNines";

    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    static extern void XamlCheckProcessRequirements();

    /// <summary>As the Windows App SDK would write it, apart from uninstalling.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        // Before anything of the app's starts, so there's no window, icon or second copy.
        if (args is [Uninstalling])
        {
            return Uninstall();
        }
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // One copy at a time, so there's one icon and one set of notifications. Another,
        // started from the Start menu or by a notification, hands what it was started for
        // to the one running, and goes.
        var running = AppInstance.FindOrRegisterForKey(RunningKey);
        if (!running.IsCurrent)
        {
            HandOver(running);
            return 0;
        }
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    /// <summary>Hands what this copy was started for to the one running, which App handles.</summary>
    static void HandOver(AppInstance running)
    {
        // Only a program someone has just started may bring a window to the front, so
        // the one running is let to, for the window it opens.
        AllowSetForegroundWindow(running.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Waited for from another thread, as this one has to go on answering COM.
        Task.Run(async () => await running.RedirectActivationToAsync(activation)).Wait();
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint processId);

    /// <summary>
    /// Turns starting at sign-in off if the app's own switch left it on, as the installer
    /// only removes what it wrote itself, and forgets the registration that lets a
    /// notification start the app, which would otherwise point at a program that's gone.
    /// Each is tried whatever happens to the other, and neither stops uninstalling: the
    /// installer only logs how it went.
    /// </summary>
    static int Uninstall()
    {
        var failed = 0;
        try
        {
            OpenAtLogin.Forget();
        }
        catch (Exception)
        {
            failed = 1;
        }
        try
        {
            AppNotificationManager.Default.UnregisterAll();
        }
        catch (Exception)
        {
            failed = 1;
        }
        return failed;
    }
}
