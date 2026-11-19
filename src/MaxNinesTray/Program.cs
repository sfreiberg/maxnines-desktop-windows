using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

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
