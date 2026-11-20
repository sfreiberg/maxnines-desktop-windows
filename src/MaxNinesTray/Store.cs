using System.ComponentModel;
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

    /// <summary>Whether it's waiting for someone to approve it in the browser.</summary>
    public bool IsSigningIn => signingIn is not null;

    /// <summary>Who it's signed in as and with which role, such as "sam@example.com (responder)".</summary>
    public string? SignedInAs => signIn switch
    {
        null => null,
        { User.Length: 0 } => "",
        { Role.Length: 0 } signedIn => signedIn.User,
        var signedIn => $"{signedIn.User} ({signedIn.Role})",
    };

    /// <summary>Whenever anything above changes.</summary>
    public event Action? Changed;

    public readonly Notifier Notifier = new();

    /// <summary>The role asked for when signing in. The person may choose a lower one.</summary>
    const string AskedRole = "responder";

    /// <summary>How long to wait for someone to approve it in the browser.</summary>
    static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    readonly KeyStore keys = new();
    AppSignIn? signIn;
    Client? client;
    CancellationTokenSource? polling;
    CancellationTokenSource? signingIn;

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
                $"acknowledge-failed-{alert}-{Guid.NewGuid()}",
                "Couldn't acknowledge the alert",
                problem,
                null,
                "/alerts")));
        Notifier.Open = Open;
        AppSignIn? resumed = null;
        string? resumeProblem = null;
        if (keys.Load() is { } refreshToken && Settings.Address.Length > 0)
        {
            try
            {
                resumed = new AppSignIn(new OAuth(Settings.Address), keys, refreshToken, Settings.User, Settings.Role);
            }
            catch (ClientException error)
            {
                resumeProblem = error.Message;
            }
        }
        Connect(resumed, resumeProblem);
    }

    public bool IsSetUp => client is not null;

    public string Headline =>
        !IsSetUp ? "Not set up" : Overview?.Headline ?? (Problem is null ? "Checking" : "Can't reach the server");

    public NotifyWhen NotifyWhen => new(Settings.NotifyCritical, Settings.NotifyWarnings, Settings.NotifyResolved);

    /// <summary>
    /// Saves how often to ask the server, which applies from the next time. Says why, if
    /// it couldn't be saved, and then nothing changes.
    /// </summary>
    public string? SaveInterval(int interval)
    {
        var changed = Settings.Copy();
        changed.IntervalSecs = interval;
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

    /// <summary>
    /// Signs in to the server at <paramref name="address"/> through the browser, and
    /// starts showing its alerts. Says why, if it didn't work, and nothing if it was
    /// cancelled. It's started from a button, so nothing it does may throw.
    /// </summary>
    public async Task<string?> SignIn(string address)
    {
        if (signingIn is not null)
        {
            return null;
        }
        OAuth oauth;
        try
        {
            oauth = new OAuth(address);
        }
        catch (ClientException error)
        {
            return error.Message;
        }
        var cancel = new CancellationTokenSource();
        signingIn = cancel;
        Changed?.Invoke();
        try
        {
            var tokens = await oauth.SignInWithBrowser(
                OpenInBrowser, Environment.MachineName, AskedRole, SignInTimeout, cancel.Token);
            return await Keep(oauth, tokens, address);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            return error is ClientException ? error.Message : $"Couldn't sign in: {error.Message}";
        }
        finally
        {
            signingIn = null;
            cancel.Dispose();
            Changed?.Invoke();
        }
    }

    /// <summary>Stops waiting for the browser.</summary>
    public void CancelSignIn() => signingIn?.Cancel();

    /// <summary>
    /// Saves a new sign-in and starts using it. If it can't be saved, it's signed out on
    /// the server too, rather than left there with nothing here to use it.
    /// </summary>
    async Task<string?> Keep(OAuth oauth, TokenResponse tokens, string address)
    {
        var changed = Settings.Copy();
        changed.Address = address.Trim();
        changed.User = tokens.User;
        changed.Role = tokens.Role;
        try
        {
            changed.Save();
            // Kept before the access token is used, as the server only takes the newest.
            keys.Save(tokens.RefreshToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            try
            {
                await oauth.Revoke(tokens.RefreshToken);
            }
            catch (ClientException)
            {
                // It can be revoked on the server, under Settings, Signed-in apps.
            }
            return $"Couldn't save the sign-in: {error.Message}";
        }
        KeyStore.ForgetApiKey();
        Settings = changed;
        Connect(new AppSignIn(oauth, keys, tokens));
        return null;
    }

    /// <summary>
    /// Forgets the sign-in here and signs out on the server, each even if the other
    /// doesn't work. Says what went wrong, if anything did.
    /// </summary>
    public async Task<string?> SignOut()
    {
        if (signIn is not { } leaving)
        {
            return null;
        }
        Connect(null);
        try
        {
            await leaving.SignOut();
            return null;
        }
        catch (SignOutFailed failed)
        {
            return SignOutProblem(failed.NotRevoked, failed.NotForgotten);
        }
        catch (Exception error)
        {
            return $"Couldn't sign out: {error.Message}";
        }
    }

    const string RevokeIt = "Revoke it in MaxNines under Settings, Signed-in apps.";

    static string SignOutProblem(Exception? notRevoked, Exception? notForgotten) => (notRevoked, notForgotten) switch
    {
        ({ } server, null) => $"Signed out here, but the server couldn't be told: {server.Message}. {RevokeIt}",
        (null, { } windows) => $"Signed out on the server, but Windows couldn't forget the sign-in: {windows.Message}",
        // Still kept, and still working on the server, it would be used again at the next start.
        ({ } server, { } windows) =>
            $"Not signed out: the server couldn't be told ({server.Message}), and Windows couldn't forget "
            + $"the sign-in ({windows.Message}), so MaxNines may be signed in again when it next starts. {RevokeIt}",
        _ => "Couldn't sign out",
    };

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

    /// <summary>Starts again with this sign-in, or none, saying <paramref name="problem"/>.</summary>
    void Connect(AppSignIn? signedIn, string? problem = null)
    {
        polling?.Cancel();
        Problem = problem;
        Overview = null;
        Updated = null;
        // Another server's alerts aren't news about this one's.
        lastAlerts = null;
        connection++;
        signIn = signedIn;
        client = signedIn is null ? null : new Client(signedIn);
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

    /// <summary>
    /// Asks the server again, and shows what it says, or why it couldn't be asked.
    /// Nothing it does throws, as the Refresh button starts it and can't wait for it.
    /// </summary>
    public async Task Refresh()
    {
        if (client is not { } asking || signIn is not { } signedIn)
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
            // Checks and silences are extras: a server too old to list them, or a role
            // not allowed to, still shows its alerts.
            var checksAsked = Optional(asking.GetRemoteChecks());
            var silencesAsked = Optional(asking.GetSilences());
            // Every one waited for, so whatever goes wrong with any of them is seen.
            await Task.WhenAll(alertsAsked, hostsAsked, checksAsked, silencesAsked);
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
        catch (Exception error)
        {
            if (!Current())
            {
                return;
            }
            answered = ask;
            if (signedIn.IsSignedOut)
            {
                // The server won't take it again, so there's nothing more to ask.
                Connect(null, error.Message);
                return;
            }
            // Such as Windows not keeping a new refresh token, which only says what it was.
            Problem = error is ClientException ? error.Message : $"Something went wrong: {error.Message}";
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
        await Refresh();
        // The action's own problem, even if the answer to asking again is dropped.
        Changed?.Invoke();
    }

    /// <summary>Opens a page of the web interface in the browser.</summary>
    public void Open(string path)
    {
        if (client is not null)
        {
            OpenPage(client.Server, path);
        }
    }

    /// <summary>
    /// Opens a page of <paramref name="server"/>'s web interface in the browser. It's
    /// started from buttons, menus and notifications, so if Windows can't open it, as
    /// when no browser is set for web pages, that's said in the flyout, and to
    /// <paramref name="failed"/>, rather than thrown.
    /// </summary>
    void OpenPage(Uri server, string path, Action<string>? failed = null)
    {
        try
        {
            OpenInBrowser(new Uri(server, path).ToString());
        }
        catch (Win32Exception error)
        {
            ActionProblem = $"Couldn't open the browser: {error.Message}";
            failed?.Invoke(ActionProblem);
            Changed?.Invoke();
        }
    }

    static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
