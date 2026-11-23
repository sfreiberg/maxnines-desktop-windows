using System.Security.Cryptography;
using System.Text;
using MaxNines.Kit;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MaxNines.Tray;

/// <summary>Shows notifications when alerts start and end, and does what their buttons say.</summary>
sealed class Notifier
{
    /// <summary>
    /// Acknowledges the alert a notification is about, with the server it came from,
    /// as the app may have signed in to another since. The server is null for one that
    /// doesn't say, as those shown before notifications said didn't.
    /// </summary>
    public Action<Uri?, long> Acknowledge = (_, _) => { };

    /// <summary>Opens a page, on the server the notification came from, as above.</summary>
    public Action<Uri?, string> Open = (_, _) => { };

    DispatcherQueue? queue;
    bool started;

    /// <summary>
    /// Registers with Windows, so a notification's buttons reach this program, and
    /// does what one says if clicking it is what started the program.
    /// </summary>
    public void Start(AppNotificationActivatedEventArgs? launchedBy)
    {
        queue = DispatcherQueue.GetForCurrentThread();
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += (_, invoked) => queue.TryEnqueue(() => Handle(invoked));
        manager.Register();
        started = true;
        if (launchedBy is not null)
        {
            Handle(launchedBy);
        }
    }

    /// <summary>
    /// Hands notification clicks back to Windows as the app quits. A notification clicked
    /// after that starts the app again to do what it says, as any app's does once it's
    /// stopped, and <see cref="Start"/> does it. Uninstalling forgets the registration
    /// altogether.
    /// </summary>
    public void Stop()
    {
        if (started)
        {
            AppNotificationManager.Default.Unregister();
        }
    }

    /// <summary>Whether Windows lets it show them: someone can turn them off in Settings.</summary>
    public bool Allowed => !started || AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    /// <summary>
    /// Shows a notification from <paramref name="server"/>, which its buttons act on
    /// whatever the app is signed in to by the time they're clicked.
    /// </summary>
    public void Post(Notice notice, Uri? server)
    {
        if (!started)
        {
            return;
        }
        var from = server?.AbsoluteUri;
        var builder = new AppNotificationBuilder()
            .AddText(notice.Title)
            .AddText(notice.Body);
        var open = new AppNotificationButton("Open in MaxNines").AddArgument("action", "open");
        AppNotificationButton? acknowledge = null;
        if (notice.Acknowledges is { } alert)
        {
            acknowledge = new AppNotificationButton("Acknowledge")
                .AddArgument("action", "acknowledge")
                .AddArgument("alert", alert.ToString());
        }
        // Clicking the notification itself, or either button, opens the same page.
        builder.AddArgument("path", notice.Path);
        open.AddArgument("path", notice.Path);
        acknowledge?.AddArgument("path", notice.Path);
        if (from is not null)
        {
            builder.AddArgument("server", from);
            open.AddArgument("server", from);
            acknowledge?.AddArgument("server", from);
        }
        if (acknowledge is not null)
        {
            builder.AddButton(acknowledge);
        }
        builder.AddButton(open);
        var built = builder.BuildNotification();
        // A tag is at most 64 characters, and a storm's ID is many joined together. The
        // same alert on two servers is two notifications, not one replacing the other.
        built.Tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{from} {notice.Id}")))[..32];
        built.Group = "alerts";
        AppNotificationManager.Default.Show(built);
    }

    /// <summary>Does what a notification, or the button clicked on it, says.</summary>
    public void Handle(AppNotificationActivatedEventArgs invoked)
    {
        var arguments = invoked.Arguments;
        var server = arguments.TryGetValue("server", out var address)
            && Uri.TryCreate(address, UriKind.Absolute, out var url)
                ? url
                : null;
        var path = arguments.TryGetValue("path", out var page) ? page : "/alerts";
        var action = arguments.TryGetValue("action", out var named) ? named : "open";
        if (action == "acknowledge" && arguments.TryGetValue("alert", out var id) && long.TryParse(id, out var alert))
        {
            Acknowledge(server, alert);
        }
        else
        {
            Open(server, path);
        }
    }
}
