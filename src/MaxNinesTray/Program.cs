using System.Runtime.InteropServices;
using MaxNines.Kit;
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
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // Before anything of the app's starts, so there's no window, icon or second copy.
        if (args is [Uninstalling])
        {
            return Uninstall();
        }
        XamlCheckProcessRequirements();
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
    /// Undoes what the app set up outside its folder, as the installer only removes what
    /// it wrote itself: starting at sign-in, if the app's own switch left it on; the
    /// registration that lets a notification start the app, which would otherwise point
    /// at a program that's gone; and the sign-in and settings, signing out on the server
    /// so the app isn't left signed in there. Each is tried whatever happens to the
    /// others, and none stops uninstalling: the installer only logs how it went.
    /// </summary>
    static int Uninstall()
    {
        var failed = 0;
        // Signed out before the settings go, as they say which server to tell.
        Action[] undoing =
        [
            OpenAtLogin.Forget,
            () => AppNotificationManager.Default.UnregisterAll(),
            SignOut,
            Settings.Forget,
        ];
        foreach (var undo in undoing)
        {
            try
            {
                undo();
            }
            catch (Exception)
            {
                failed = 1;
            }
        }
        return failed;
    }

    /// <summary>
    /// Forgets the sign-in, then signs out on the server, waiting for it no longer than
    /// signing in waits for the server.
    /// </summary>
    static void SignOut()
    {
        KeyStore.ForgetApiKey();
        var keys = new KeyStore();
        if (keys.Load() is not { } refreshToken)
        {
            return;
        }
        var settings = Settings.Load();
        OAuth oauth;
        try
        {
            oauth = new OAuth(settings.Address);
        }
        catch (ClientException)
        {
            // There's no server to tell.
            keys.Save(null);
            throw;
        }
        var signIn = new AppSignIn(oauth, keys, refreshToken, settings.User, settings.Role);
        Task.Run(() => signIn.SignOut()).GetAwaiter().GetResult();
    }
}
