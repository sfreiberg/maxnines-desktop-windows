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
    public Action<long> Acknowledge = _ => { };
    public Action<string> Open = _ => { };

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
        manager.NotificationInvoked += (_, invoked) => queue.TryEnqueue(() => Handle(invoked.Arguments));
        manager.Register();
        started = true;
        if (launchedBy is not null)
        {
            Handle(launchedBy.Arguments);
        }
    }

    /// <summary>Stops the buttons of notifications still showing from starting it again.</summary>
    public void Stop()
    {
        if (started)
        {
            AppNotificationManager.Default.Unregister();
        }
    }

    /// <summary>Whether Windows lets it show them: someone can turn them off in Settings.</summary>
    public bool Allowed => !started || AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    public void Post(Notice notice)
    {
        if (!started)
        {
            return;
        }
        var builder = new AppNotificationBuilder()
            .AddArgument("path", notice.Path)
            .AddText(notice.Title)
            .AddText(notice.Body);
        if (notice.Acknowledges is { } alert)
        {
            builder.AddButton(new AppNotificationButton("Acknowledge")
                .AddArgument("action", "acknowledge")
                .AddArgument("alert", alert.ToString())
                .AddArgument("path", notice.Path));
        }
        builder.AddButton(new AppNotificationButton("Open in MaxNines")
            .AddArgument("action", "open")
            .AddArgument("path", notice.Path));
        var built = builder.BuildNotification();
        // A tag is at most 64 characters, and a storm's ID is many joined together.
        built.Tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice.Id)))[..32];
        built.Group = "alerts";
        AppNotificationManager.Default.Show(built);
    }

    void Handle(IDictionary<string, string> arguments)
    {
        var path = arguments.TryGetValue("path", out var page) ? page : "/alerts";
        var action = arguments.TryGetValue("action", out var named) ? named : "open";
        if (action == "acknowledge" && arguments.TryGetValue("alert", out var id) && long.TryParse(id, out var alert))
        {
            Acknowledge(alert);
        }
        else
        {
            Open(path);
        }
    }
}
