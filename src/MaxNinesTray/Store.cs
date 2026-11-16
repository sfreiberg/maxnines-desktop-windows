using System.Diagnostics;
using System.Runtime.InteropServices;
using MaxNines.Kit;

namespace MaxNines.Tray;

/// <summary>What the app knows: its settings, and what the server last said.</summary>
sealed class Store
{
    public static readonly int[] Intervals = [15, 30, 60];

    /// <summary>
    /// What the server last listed. Kept while a later try fails, as old news that
    /// says it's old beats none.
    /// </summary>
    public Overview? Overview { get; private set; }

    public DateTimeOffset? Updated { get; private set; }

    /// <summary>Why the server couldn't be asked, if it couldn't.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// Something else that didn't work, such as the last acknowledge or silence, shown
    /// until it's dismissed.
    /// </summary>
    public string? ActionProblem { get; set; }

    public Settings Settings { get; private set; }

    /// <summary>
    /// Whether a key is saved. Kept here, as asking Credential Manager on every
    /// keystroke in Settings is slow.
    /// </summary>
    public bool HasApiKey { get; private set; }

    /// <summary>Whenever anything above changes.</summary>
    public event Action? Changed;

    public readonly Notifier Notifier = new();

    Client? client;
    CancellationTokenSource? polling;

    /// <summary>
    /// The alerts the server listed last time, which notifications are worked out
    /// against. Nothing until the first answer, so what was already firing isn't
    /// announced.
    /// </summary>
    Alerts? lastAlerts;

    /// <summary>Counts each change of settings, so an answer to the old ones is dropped.</summary>
    int connection;

    /// <summary>
    /// Counts each time the server is asked, and which of those answers was used last,
    /// so an answer overtaken by a later one is dropped. Several can be asking at once:
    /// the regular check, Refresh, and the one after each action.
    /// </summary>
    int asks;
    int answered;

    public Store()
    {
        Settings = Settings.Load();
        if (!Intervals.Contains(Settings.IntervalSecs))
        {
            Settings.IntervalSecs = Intervals[0];
        }
        // Acknowledged from a notification, so a notification says if it didn't work:
        // the flyout, where it would otherwise show, may never be opened.
        Notifier.Acknowledge = alert => Act(
            client => client.Acknowledge(alert),
            problem => Post(new Notice(
                $"acknowledge-failed-{alert}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                "Couldn't acknowledge the alert",
                problem,
                null,
                "/alerts")));
        Notifier.Open = Open;
        var apiKey = KeyStore.ApiKey() ?? "";
        HasApiKey = apiKey.Length > 0;
        Connect(apiKey);
    }

    public bool IsSetUp => client is not null;

    public string Headline =>
        !IsSetUp ? "Not set up" : Overview?.Headline ?? (Problem is null ? "Checking" : "Can't reach the server");

    public NotifyWhen NotifyWhen => new(Settings.NotifyCritical, Settings.NotifyWarnings, Settings.NotifyResolved);

    /// <summary>
    /// Saves the settings and starts again with them. An empty key keeps the one that's
    /// saved. Says why, if they couldn't be saved, and then nothing changes.
    /// </summary>
    public string? Save(string address, string apiKey, int interval)
    {
        var changed = Settings.Copy();
        changed.Address = address.Trim();
        changed.IntervalSecs = interval;
        try
        {
            changed.Save();
            if (apiKey.Length > 0)
            {
                KeyStore.SetApiKey(apiKey.Trim());
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            return $"Couldn't save the settings: {error.Message}";
        }
        Settings = changed;
        var saved = KeyStore.ApiKey() ?? "";
        HasApiKey = saved.Length > 0;
        Overview = null;
        Updated = null;
        Connect(saved);
        return null;
    }

    /// <summary>
    /// Saves which notifications are wanted, which apply at once. Says why, if they
    /// couldn't be saved, and then nothing changes.
    /// </summary>
    public string? SaveNotifications(NotifyWhen when)
    {
        var changed = Settings.Copy();
        changed.NotifyCritical = when.Critical;
        changed.NotifyWarnings = when.Warnings;
        changed.NotifyResolved = when.Resolved;
        try
        {
            changed.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save the settings: {error.Message}";
        }
        Settings = changed;
        return null;
    }

    void Connect(string apiKey)
    {
        polling?.Cancel();
        Problem = null;
        // Another server's alerts aren't news about this one's.
        lastAlerts = null;
        connection++;
        client = null;
        if (apiKey.Length > 0 && Settings.Address.Length > 0)
        {
            try
            {
                client = new Client(Settings.Address, apiKey);
            }
            catch (ClientException error)
            {
                Problem = error.Message;
            }
        }
        Changed?.Invoke();
        if (client is null)
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        polling = cancel;
        _ = Poll(cancel.Token);
    }

    /// <summary>
    /// Runs on the window thread, so everything it changes is safe to show. Nothing
    /// that goes wrong stops it, or the icon would go on showing old news as current.
    /// </summary>
    async Task Poll(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await Refresh();
            }
            catch (Exception error)
            {
                Problem = $"Something went wrong: {error.Message}";
                Changed?.Invoke();
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Settings.IntervalSecs), cancel);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    public async Task Refresh()
    {
        if (client is not { } asking)
        {
            return;
        }
        var asked = connection;
        var ask = ++asks;
        // Whether this is still the newest answer for the settings now in use.
        bool Current() => asked == connection && ask > answered;
        try
        {
            var alertsAsked = asking.GetAlerts();
            var hostsAsked = asking.GetHosts();
            // Checks and silences are extras: a server too old to list them, or a key
            // not allowed to, still shows its alerts.
            var checksAsked = Optional(asking.GetRemoteChecks());
            var silencesAsked = Optional(asking.GetSilences());
            await Task.WhenAll(alertsAsked, hostsAsked);
            var alerts = await alertsAsked;
            var hosts = await hostsAsked;
            var checks = await checksAsked;
            var silences = await silencesAsked;
            if (!Current())
            {
                return;
            }
            answered = ask;
            Overview = new Overview(alerts, hosts, checks, silences?.Active);
            Updated = DateTimeOffset.UtcNow;
            Problem = null;
            var last = lastAlerts;
            lastAlerts = alerts;
            if (last is not null)
            {
                foreach (var notice in Notices.Between(last, alerts, NotifyWhen))
                {
                    Post(notice);
                }
            }
        }
        catch (ClientException error)
        {
            if (!Current())
            {
                return;
            }
            answered = ask;
            Problem = error.Message;
        }
        Changed?.Invoke();
    }

    /// <summary>Shows a notification, saying so in the flyout if Windows won't.</summary>
    void Post(Notice notice)
    {
        try
        {
            Notifier.Post(notice);
        }
        catch (Exception error)
        {
            ActionProblem = $"Windows wouldn't show a notification: {error.Message}";
            Changed?.Invoke();
        }
    }

    static async Task<T?> Optional<T>(Task<T> asking) where T : class
    {
        try
        {
            return await asking;
        }
        catch (ClientException)
        {
            return null;
        }
    }

    /// <summary>Tries settings without saving them, and says how it went.</summary>
    public async Task<(bool Worked, string Message)> Test(string address, string apiKey)
    {
        var key = apiKey.Length == 0 ? KeyStore.ApiKey() ?? "" : apiKey;
        try
        {
            var hosts = (await new Client(address, key).GetHosts()).Count(host => host.IsCounted);
            return (true, $"Connected. It has {hosts} {(hosts == 1 ? "host" : "hosts")}.");
        }
        catch (ClientException error)
        {
            return (false, error.Message);
        }
    }

    public void Acknowledge(ServerAlert alert) => Act(client => client.Acknowledge(alert.Id));

    public void SilenceForAnHour(ServerAlert alert) => Act(client => client.Silence(alert, TimeSpan.FromHours(1)));

    /// <summary>
    /// Does something on the server, then asks it again to show the result.
    /// <paramref name="failed"/> is told why it didn't work, as well as the flyout.
    /// It's started from buttons, which can't wait for it, so nothing it does may throw.
    /// </summary>
    async void Act(Func<Client, Task> action, Action<string>? failed = null)
    {
        if (client is not { } acting)
        {
            failed?.Invoke("MaxNines isn't set up");
            return;
        }
        try
        {
            await action(acting);
            ActionProblem = null;
        }
        catch (Exception error)
        {
            ActionProblem = error.Message;
            failed?.Invoke(error.Message);
        }
        try
        {
            await Refresh();
        }
        catch (Exception error)
        {
            Problem = $"Something went wrong: {error.Message}";
        }
        Changed?.Invoke();
    }

    /// <summary>Opens a page of the web interface in the browser.</summary>
    public void Open(string path)
    {
        if (client is not null)
        {
            Process.Start(new ProcessStartInfo(client.Page(path).ToString()) { UseShellExecute = true });
        }
    }
}
