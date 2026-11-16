using System.Diagnostics;
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

    /// <summary>Why the last acknowledge or silence didn't work.</summary>
    public string? ActionProblem { get; set; }

    public Settings Settings { get; private set; }

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

    public Store()
    {
        Settings = Settings.Load();
        if (!Intervals.Contains(Settings.IntervalSecs))
        {
            Settings.IntervalSecs = Intervals[0];
        }
        Notifier.Acknowledge = alert => Act(client => client.Acknowledge(alert));
        Notifier.Open = Open;
        Connect(KeyStore.ApiKey() ?? "");
    }

    public bool IsSetUp => client is not null;
    public bool HasApiKey => !string.IsNullOrEmpty(KeyStore.ApiKey());
    public bool SendsKeyInTheClear => client?.SendsKeyInTheClear ?? false;

    public string Headline =>
        !IsSetUp ? "Not set up" : Overview?.Headline ?? (Problem is null ? "Checking" : "Can't reach the server");

    public NotifyWhen NotifyWhen => new(Settings.NotifyCritical, Settings.NotifyWarnings, Settings.NotifyResolved);

    /// <summary>Saves the settings and starts again with them. An empty key keeps the one that's saved.</summary>
    public void Save(string address, string apiKey, int interval)
    {
        Settings.Address = address.Trim();
        Settings.IntervalSecs = interval;
        Settings.Save();
        if (apiKey.Length > 0)
        {
            KeyStore.SetApiKey(apiKey.Trim());
        }
        Overview = null;
        Updated = null;
        Connect(KeyStore.ApiKey() ?? "");
    }

    /// <summary>Saves which notifications are wanted, which apply at once.</summary>
    public void SaveNotifications(NotifyWhen when)
    {
        Settings.NotifyCritical = when.Critical;
        Settings.NotifyWarnings = when.Warnings;
        Settings.NotifyResolved = when.Resolved;
        Settings.Save();
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

    /// <summary>Runs on the window thread, so everything it changes is safe to show.</summary>
    async Task Poll(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            await Refresh();
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
            // The settings changed while it was asking.
            if (asked != connection)
            {
                return;
            }
            Overview = new Overview(alerts, hosts, checks, silences?.Active);
            Updated = DateTimeOffset.UtcNow;
            Problem = null;
            if (lastAlerts is not null)
            {
                foreach (var notice in Notices.Between(lastAlerts, alerts, NotifyWhen))
                {
                    Notifier.Post(notice);
                }
            }
            lastAlerts = alerts;
        }
        catch (ClientException error)
        {
            if (asked != connection)
            {
                return;
            }
            Problem = error.Message;
        }
        Changed?.Invoke();
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

    async void Act(Func<Client, Task> action)
    {
        if (client is not { } acting)
        {
            return;
        }
        try
        {
            await action(acting);
            ActionProblem = null;
        }
        catch (ClientException error)
        {
            ActionProblem = error.Message;
        }
        await Refresh();
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
