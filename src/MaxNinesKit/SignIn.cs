using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MaxNines.Kit;

/// <summary>
/// The proof that whoever swaps a code for tokens is whoever asked for it (RFC 7636):
/// a random verifier, and the challenge made from it, which is sent first.
/// </summary>
public static class Pkce
{
    /// <summary>32 random bytes in base64url: 43 characters, all ones a URL leaves alone.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The SHA-256 of the verifier in base64url, which the server calls S256.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Sent with the request and back with the answer, to check the answer is to it.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What the server gives when it signs the app in, and each time it refreshes.</summary>
public sealed class TokenResponse
{
    public string AccessToken { get; init; } = "";

    /// <summary>How many seconds the access token works for.</summary>
    public long ExpiresIn { get; init; }

    public string RefreshToken { get; init; } = "";

    /// <summary>The most the app may do, which the person may have chosen lower than asked.</summary>
    public string Role { get; init; } = "";

    /// <summary>Who it acts as: their email address.</summary>
    public string User { get; init; } = "";
}

/// <summary>How the server says no, as OAuth words it.</summary>
sealed class OAuthError
{
    public string Error { get; init; } = "";
    public string ErrorDescription { get; init; } = "";
}

/// <summary>Signs the app in to one server through the browser, and swaps its tokens there.</summary>
public sealed class OAuth
{
    /// <summary>Which app this is, as the server knows it.</summary>
    public const string ClientId = "maxnines-desktop-windows";

    public Uri Server { get; }
    readonly HttpClient http;

    /// <param name="address">What someone types: the address they open MaxNines at.</param>
    public OAuth(string address, HttpMessageHandler? handler = null)
    {
        Server = Client.SiteRoot(address);
        http = handler is null ? Client.Shared : new HttpClient(handler);
    }

    /// <summary>The page where someone approves the app, which the browser is opened at.</summary>
    public string AuthorizeUrl(string redirectUri, string challenge, string state, string device, string role)
    {
        (string Name, string Value)[] fields =
        [
            ("response_type", "code"),
            ("client_id", ClientId),
            ("redirect_uri", redirectUri),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("state", state),
            ("device", device),
            ("role", role),
        ];
        var query = string.Join('&', fields.Select(field => $"{field.Name}={Uri.EscapeDataString(field.Value)}"));
        return $"{Server.AbsoluteUri}oauth/authorize?{query}";
    }

    /// <summary>
    /// Signs in through the browser: listens on this computer, has
    /// <paramref name="openBrowser"/> open the page where someone approves the app,
    /// waits up to <paramref name="timeout"/> for their answer, and swaps it for tokens.
    /// </summary>
    /// <param name="device">This computer's name, shown when approving and on the server.</param>
    /// <param name="role">The role to ask for. The person may choose a lower one.</param>
    public async Task<TokenResponse> SignInWithBrowser(
        Action<string> openBrowser, string device, string role, TimeSpan timeout, CancellationToken cancel = default)
    {
        using var loopback = new Loopback();
        var verifier = Pkce.NewVerifier();
        var state = Pkce.NewState();
        openBrowser(AuthorizeUrl(loopback.RedirectUri, Pkce.Challenge(verifier), state, device, role));
        TokenResponse tokens;
        try
        {
            // The code is swapped before the browser is answered, so its page only says
            // signed in once the app is. Cancelling doesn't stop the swap once it's
            // started, as the server may already have signed the app in: what it brings
            // is waited for, and signed out below.
            tokens = await loopback.Wait(
                state,
                timeout,
                answer => answer.Code is { } code
                    ? Redeem(code, loopback.RedirectUri, verifier, CancellationToken.None)
                    : throw (answer.Error == "access_denied"
                        ? new ClientException(ClientProblem.SignInDenied)
                        : new ClientException(ClientProblem.SignInFailed, detail: answer.Error)),
                cancel);
        }
        catch (Exception error) when (error is not OperationCanceledException && cancel.IsCancellationRequested)
        {
            // Cancelled, and the swap didn't work anyway: there's nothing to undo or say.
            throw new OperationCanceledException(cancel);
        }
        if (cancel.IsCancellationRequested)
        {
            // Cancelled while the code was being swapped: nothing here will keep these,
            // so they mustn't stay signed in on the server.
            try
            {
                await Revoke(tokens.RefreshToken, CancellationToken.None);
            }
            catch (ClientException error)
            {
                throw new ClientException(ClientProblem.SignInNotUndone, inner: error);
            }
            throw new OperationCanceledException(cancel);
        }
        return tokens;
    }

    /// <summary>Swaps the code the browser brought back for tokens.</summary>
    public Task<TokenResponse> Redeem(string code, string redirectUri, string verifier, CancellationToken cancel = default) =>
        Exchange(
            [
                new("grant_type", "authorization_code"),
                new("client_id", ClientId),
                new("code", code),
                new("redirect_uri", redirectUri),
                new("code_verifier", verifier),
            ],
            refreshing: false,
            cancel);

    /// <summary>
    /// Swaps a refresh token for new tokens. The one swapped stops working, so only the
    /// new one may be kept. If the server won't take it, the app has been signed out.
    /// </summary>
    public Task<TokenResponse> Refresh(string refreshToken, CancellationToken cancel = default) =>
        Exchange(
            [
                new("grant_type", "refresh_token"),
                new("client_id", ClientId),
                new("refresh_token", refreshToken),
            ],
            refreshing: true,
            cancel);

    /// <summary>Signs out the sign-in that holds this token.</summary>
    public async Task Revoke(string token, CancellationToken cancel = default)
    {
        var (status, _) = await Post("oauth/revoke", [new("client_id", ClientId), new("token", token)], cancel);
        if (status is not (>= 200 and < 300))
        {
            throw new ClientException(ClientProblem.Status, status);
        }
    }

    async Task<TokenResponse> Exchange(
        KeyValuePair<string?, string?>[] form, bool refreshing, CancellationToken cancel)
    {
        var (status, text) = await Post("oauth/token", form, cancel);
        if (status is >= 200 and < 300)
        {
            var tokens = Decode<TokenResponse>(text);
            if (tokens is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 })
            {
                throw new ClientException(ClientProblem.NotMaxNines);
            }
            return tokens;
        }
        if (status is 400 or 401 && Decode<OAuthError>(text) is { Error.Length: > 0 } error)
        {
            if (refreshing && error.Error == "invalid_grant")
            {
                throw new ClientException(ClientProblem.NotSignedIn);
            }
            throw new ClientException(
                ClientProblem.SignInFailed,
                detail: error.ErrorDescription.Length > 0 ? error.ErrorDescription : error.Error);
        }
        throw new ClientException(ClientProblem.Status, status);
    }

    static T? Decode<T>(string text) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Client.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How long one sign-in request waits for its answer: short, so a refresh that gets
    /// none can be tried again while the server still takes its token. See
    /// <see cref="AppSignIn.RetryWindow"/>.
    /// </summary>
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A form, as OAuth sends everything, and the status and body of the answer.</summary>
    async Task<(int Status, string Text)> Post(
        string path, KeyValuePair<string?, string?>[] form, CancellationToken cancel)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(RequestTimeout);
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(new Uri(Server, path), content, limit.Token);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(limit.Token));
        }
        catch (HttpRequestException error)
        {
            throw new ClientException(ClientProblem.Unreachable, inner: error);
        }
        catch (TaskCanceledException error) when (!cancel.IsCancellationRequested)
        {
            // Timed out.
            throw new ClientException(ClientProblem.Unreachable, inner: error);
        }
    }
}

/// <summary>Keeps the newest refresh token somewhere that lasts, such as Credential Manager.</summary>
public interface IRefreshTokenStore
{
    /// <summary>Keeps this one in place of any before it, or forgets it when it's null.</summary>
    void Save(string? refreshToken);
}

/// <summary>
/// The app signed in to a server: the access token, kept only in memory, and the
/// refresh token, kept in the store. Swaps the refresh token for new tokens a minute
/// before the access token runs out, or when the server turns it away.
/// </summary>
/// <remarks>
/// Only one swap runs at a time, and everyone who needs one while it runs waits for
/// it. That matters: each swap makes the refresh token before it stop working, and if
/// that one is used again more than a minute later the server takes it as stolen and
/// signs the app out. For the same reason a swap whose answer was lost is tried again
/// within that minute.
/// </remarks>
public sealed class AppSignIn
{
    /// <summary>How long before the access token runs out it's swapped.</summary>
    internal static readonly TimeSpan Early = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long to wait before each new try of a swap that failed, all inside the minute
    /// the server still takes a refresh token it has just replaced.
    /// </summary>
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    /// <summary>
    /// How long after the first try a refresh may start another. The server takes a
    /// refresh token it has just replaced for a minute from when it replaced it, which is
    /// no earlier than the first try, so this leaves room for a slow network, however
    /// long each try waited for its answer.
    /// </summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(45);

    readonly OAuth oauth;
    readonly IRefreshTokenStore store;
    readonly TimeProvider time;
    readonly IReadOnlyList<TimeSpan> retryDelays;
    readonly object gate = new();

    /// <summary>Cancelled when signing out starts, which ends any waiting to try a swap again.</summary>
    readonly CancellationTokenSource signOutStarted = new();

    // Everything below is only touched holding the gate, and so is the store, so once
    // signing out has forgotten the token, nothing from this sign-in is kept again.
    string? refreshToken;
    string? accessToken;
    DateTimeOffset accessExpires;
    /// <summary>The swap running now, which anyone else needing one waits for.</summary>
    Task<string>? refreshing;
    bool signingOut;

    /// <summary>Signed in before, with only the refresh token kept from then.</summary>
    /// <param name="retryDelays">How long to wait before each new try of a failed swap.</param>
    public AppSignIn(
        OAuth oauth,
        IRefreshTokenStore store,
        string refreshToken,
        string user,
        string role,
        TimeProvider? time = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        this.oauth = oauth;
        this.store = store;
        this.time = time ?? TimeProvider.System;
        this.retryDelays = retryDelays ?? RetryDelays;
        this.refreshToken = refreshToken;
        User = user;
        Role = role;
    }

    /// <summary>Just signed in. The refresh token must already be in the store.</summary>
    public AppSignIn(
        OAuth oauth,
        IRefreshTokenStore store,
        TokenResponse tokens,
        TimeProvider? time = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
        : this(oauth, store, tokens.RefreshToken, tokens.User, tokens.Role, time, retryDelays)
    {
        accessToken = tokens.AccessToken;
        accessExpires = this.time.GetUtcNow() + TimeSpan.FromSeconds(tokens.ExpiresIn);
    }

    public Uri Server => oauth.Server;

    /// <summary>Who it acts as, as the server last said.</summary>
    public string User { get; private set; }

    /// <summary>The most it may do, as the server last said.</summary>
    public string Role { get; private set; }

    /// <summary>Whether the server signed it out, or it was signed out here.</summary>
    public bool IsSignedOut
    {
        get
        {
            lock (gate)
            {
                return refreshToken is null || signingOut;
            }
        }
    }

    /// <summary>An access token that works for at least another minute, swapping for one if needed.</summary>
    public Task<string> AccessToken(CancellationToken cancel = default) => Refreshed(null, cancel);

    /// <summary>A new access token, as the server turned <paramref name="rejected"/> away.</summary>
    public Task<string> Rejected(string rejected, CancellationToken cancel = default) => Refreshed(rejected, cancel);

    Task<string> Refreshed(string? rejected, CancellationToken cancel)
    {
        TaskCompletionSource<string>? started = null;
        string swapping;
        Task<string> running;
        lock (gate)
        {
            if (refreshToken is not { } current || signingOut)
            {
                return Task.FromException<string>(new ClientException(ClientProblem.NotSignedIn));
            }
            // Fine as it is, or already swapped by someone else after the server turned
            // theirs away.
            if (accessToken is { } token && token != rejected && time.GetUtcNow() < accessExpires - Early)
            {
                return Task.FromResult(token);
            }
            if (refreshing is null)
            {
                started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                refreshing = started.Task;
            }
            swapping = current;
            running = refreshing;
        }
        if (started is not null)
        {
            _ = Refresh(swapping, started);
        }
        // Whoever stops waiting doesn't stop the swap: its answer must still be kept.
        return running.WaitAsync(cancel);
    }

    /// <summary>The one swap running. Nothing it does throws: it all goes to whoever waits.</summary>
    async Task Refresh(string swapping, TaskCompletionSource<string> done)
    {
        try
        {
            var tokens = await RefreshTrying(swapping);
            lock (gate)
            {
                // The one just swapped no longer works, whatever happens next.
                refreshToken = tokens.RefreshToken;
                // Kept before the access token is used, so a crash can't leave a stale one.
                // Not once signing out has started, as that has forgotten this sign-in, and
                // a newer one may have been kept in its place since.
                if (!signingOut)
                {
                    store.Save(tokens.RefreshToken);
                }
                accessToken = tokens.AccessToken;
                accessExpires = time.GetUtcNow() + TimeSpan.FromSeconds(tokens.ExpiresIn);
                User = tokens.User;
                Role = tokens.Role;
                refreshing = null;
            }
            done.SetResult(tokens.AccessToken);
        }
        catch (ClientException error) when (error.Problem == ClientProblem.NotSignedIn)
        {
            lock (gate)
            {
                refreshToken = null;
                accessToken = null;
                refreshing = null;
                // Signing out has already forgotten it, and a newer sign-in may be kept now.
                if (!signingOut)
                {
                    try
                    {
                        store.Save(null);
                    }
                    catch (Exception)
                    {
                        // Still kept, it's turned away again next time, and forgotten then.
                    }
                }
            }
            done.SetException(error);
        }
        catch (Exception error)
        {
            // Not swapped, or swapped but not kept: either way the newest refresh token
            // is in memory for the next try.
            lock (gate)
            {
                refreshing = null;
            }
            done.SetException(error);
        }
    }

    /// <summary>
    /// Swaps the refresh token, trying the same one again after each of the delays if
    /// the answer doesn't come back, as it may have been lost after the server swapped
    /// it. Never again once the server has said no, or signing out has started.
    /// </summary>
    async Task<TokenResponse> RefreshTrying(string swapping)
    {
        var started = time.GetUtcNow();
        for (var tries = 0; ; tries++)
        {
            Exception failed;
            try
            {
                return await oauth.Refresh(swapping, CancellationToken.None);
            }
            catch (ClientException error) when (error.Problem == ClientProblem.NotSignedIn)
            {
                throw;
            }
            catch (Exception error)
            {
                failed = error;
            }
            if (tries >= retryDelays.Count
                || signOutStarted.IsCancellationRequested
                || time.GetUtcNow() - started + retryDelays[tries] >= RetryWindow)
            {
                ExceptionDispatchInfo.Throw(failed);
            }
            try
            {
                await Task.Delay(retryDelays[tries], signOutStarted.Token);
            }
            catch (OperationCanceledException)
            {
                ExceptionDispatchInfo.Throw(failed);
            }
        }
    }

    /// <summary>
    /// Forgets the refresh token, then signs out on the server. It's forgotten at once,
    /// before anything is waited for, so signing out can never forget a sign-in made
    /// after it began. If the server can't be told, or the token can't be forgotten,
    /// <see cref="SignOutFailed"/> says which, once both have been tried.
    /// </summary>
    public async Task SignOut(CancellationToken cancel = default)
    {
        Task<string>? running;
        Exception? notForgotten = null;
        lock (gate)
        {
            signingOut = true;
            running = refreshing;
            try
            {
                store.Save(null);
            }
            catch (Exception error)
            {
                notForgotten = error;
            }
        }
        signOutStarted.Cancel();
        // The server only knows the newest token, so a swap running has to finish first.
        if (running is not null)
        {
            try
            {
                await running;
            }
            catch (Exception)
            {
                // Then the token from before it is still the newest.
            }
        }
        string? token;
        lock (gate)
        {
            token = refreshToken;
            refreshToken = null;
            accessToken = null;
        }
        Exception? notRevoked = null;
        if (token is not null)
        {
            try
            {
                await oauth.Revoke(token, cancel);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                notRevoked = error;
            }
        }
        if (notRevoked is not null || notForgotten is not null)
        {
            throw new SignOutFailed(notRevoked, notForgotten);
        }
    }
}

/// <summary>Signing out didn't all work: the server wasn't told, the token wasn't forgotten, or both.</summary>
public sealed class SignOutFailed(Exception? notRevoked, Exception? notForgotten)
    : Exception((notRevoked ?? notForgotten)?.Message, notRevoked ?? notForgotten)
{
    /// <summary>Why the server couldn't be told, so the sign-in still works there until it's revoked.</summary>
    public Exception? NotRevoked { get; } = notRevoked;

    /// <summary>Why the refresh token couldn't be forgotten, so it's still kept here.</summary>
    public Exception? NotForgotten { get; } = notForgotten;
}

/// <summary>What the browser came back with: a code, or why there isn't one.</summary>
public sealed record Callback(string? Code, string? Error)
{
    public const string Path = "/callback";

    /// <summary>
    /// The answer in a request's target, such as <c>/callback?code=…&amp;state=…</c>, if
    /// it's an answer to the request with <paramref name="state"/>.
    /// </summary>
    public static Callback? Parse(string target, string state)
    {
        var question = target.IndexOf('?');
        if (question < 0 || target[..question] != Path)
        {
            return null;
        }
        var fields = new Dictionary<string, string>();
        foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? "" : pair[(equals + 1)..];
            fields.TryAdd(Unescape(name), Unescape(value));
        }
        if (fields.GetValueOrDefault("state") != state)
        {
            return null;
        }
        if (fields.GetValueOrDefault("code") is { Length: > 0 } code)
        {
            return new Callback(code, null);
        }
        return fields.GetValueOrDefault("error") is { Length: > 0 } error ? new Callback(null, error) : null;
    }

    static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}

/// <summary>
/// Where the browser comes back to after someone approves the app: a free port on
/// 127.0.0.1, written as the address since the server won't take <c>localhost</c>.
/// It answers with a page saying how it went.
/// </summary>
public sealed class Loopback : IDisposable
{
    /// <summary>How long a connection gets to send its request.</summary>
    static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    readonly TcpListener listener;

    /// <summary>Starts listening.</summary>
    public Loopback()
    {
        // A plain socket, as HttpListener can need an administrator to allow its address.
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string RedirectUri => $"http://127.0.0.1:{Port}{Callback.Path}";

    public void Dispose() => listener.Stop();

    /// <summary>
    /// Waits for the answer to the request with <paramref name="state"/>. Anything else
    /// that connects, such as a browser asking for an icon or opening a connection it
    /// never uses, is answered and passed over.
    /// </summary>
    public Task<Callback> Wait(string state, TimeSpan timeout, CancellationToken cancel = default) =>
        Wait(state, timeout, Task.FromResult, cancel);

    /// <summary>
    /// Waits for the answer to the request with <paramref name="state"/>, and has
    /// <paramref name="finish"/> act on it before the browser is answered, so the page
    /// says how it went: signed in only if <paramref name="finish"/> worked.
    /// </summary>
    public async Task<T> Wait<T>(
        string state, TimeSpan timeout, Func<Callback, Task<T>> finish, CancellationToken cancel = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stop.CancelAfter(timeout);
        var answered = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taken = new Taken(() => stop.CancelAfter(Timeout.InfiniteTimeSpan));
        try
        {
            _ = Accept(state, finish, taken, answered, stop.Token);
            try
            {
                return await answered.Task.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                if (taken.Close())
                {
                    throw;
                }
                // The answer came first, and is being acted on: what that gives has to be
                // kept or undone, never dropped, so it's waited for.
                return await answered.Task;
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ClientException(ClientProblem.SignInTimedOut);
        }
        finally
        {
            stop.Cancel();
        }
    }

    /// <summary>
    /// Whether the answer has arrived, so only the first is acted on, or waiting has
    /// stopped, after which none is. Taking it stops the clock: the time limit is for
    /// someone to answer in the browser, not for swapping what they answered, which
    /// would leave a sign-in on the server that nothing here holds.
    /// </summary>
    sealed class Taken(Action stopClock)
    {
        const int Waiting = 0, Answered = 1, Stopped = 2;
        int state;

        /// <summary>Takes the answer, unless one has been already or waiting has stopped.</summary>
        public bool Take()
        {
            if (Interlocked.CompareExchange(ref state, Answered, Waiting) != Waiting)
            {
                return false;
            }
            stopClock();
            return true;
        }

        /// <summary>Stops waiting, unless an answer has been taken, which it says by returning false.</summary>
        public bool Close() => Interlocked.CompareExchange(ref state, Stopped, Waiting) != Answered;
    }

    /// <summary>Takes each connection until there's an answer. Nothing it does throws.</summary>
    async Task Accept<T>(
        string state, Func<Callback, Task<T>> finish, Taken taken, TaskCompletionSource<T> answered,
        CancellationToken stop)
    {
        while (!answered.Task.IsCompleted)
        {
            TcpClient connection;
            try
            {
                connection = await listener.AcceptTcpClientAsync(stop);
            }
            catch (Exception error)
            {
                if (!stop.IsCancellationRequested)
                {
                    answered.TrySetException(error);
                }
                return;
            }
            // Each on its own, so one that never sends anything holds nothing up.
            _ = Answer(connection, state, finish, taken, answered, stop);
        }
    }

    /// <summary>
    /// Reads one request, acts on it if it's the answer, and replies. Nothing it does
    /// throws: what <c>finish</c> gives or throws goes to <c>answered</c>.
    /// </summary>
    static async Task Answer<T>(
        TcpClient connection, string state, Func<Callback, Task<T>> finish, Taken taken,
        TaskCompletionSource<T> answered, CancellationToken stop)
    {
        Callback? callback = null;
        Task<T>? finishing = null;
        try
        {
            using (connection)
            {
                var stream = connection.GetStream();
                string? target;
                using (var reading = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    reading.CancelAfter(ReadTimeout);
                    target = await ReadTarget(stream, reading.Token);
                }
                callback = target is null ? null : Callback.Parse(target, state);
                // A second answer to the same request isn't acted on again.
                if (callback is not null && !taken.Take())
                {
                    callback = null;
                }
                string? failure = null;
                if (callback is not null)
                {
                    finishing = Finish(finish, callback);
                    try
                    {
                        await finishing;
                    }
                    catch (Exception error)
                    {
                        failure = error.Message;
                    }
                }
                using var writing = CancellationTokenSource.CreateLinkedTokenSource(stop);
                writing.CancelAfter(ReadTimeout);
                var reply = Reply(target, callback, failure);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), writing.Token);
            }
        }
        catch (Exception)
        {
            // The browser went away, or it wasn't a browser: the answer, if this was
            // it, still counts.
        }
        if (finishing is not null)
        {
            try
            {
                answered.TrySetResult(await finishing);
            }
            catch (Exception error)
            {
                answered.TrySetException(error);
            }
        }
    }

    /// <summary>Runs <c>finish</c>, with anything it throws straight away kept in the task.</summary>
    static async Task<T> Finish<T>(Func<Callback, Task<T>> finish, Callback callback) => await finish(callback);

    /// <summary>
    /// The target of a GET request, such as <c>/callback?code=…</c>, once all its headers
    /// have arrived, as closing with some unread could cut the page off.
    /// </summary>
    static async Task<string?> ReadTarget(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancel);
            if (read == 0)
            {
                return null;
            }
            length += read;
            var text = Encoding.Latin1.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var parts = text[..text.IndexOf("\r\n", StringComparison.Ordinal)].Split(' ');
                return parts is ["GET", var target, _] ? target : null;
            }
        }
        return null;
    }

    /// <summary>
    /// The whole HTTP answer: a page for the browser, or nothing for anything else.
    /// <paramref name="failure"/> is why acting on the answer didn't work, if it didn't.
    /// </summary>
    internal static string Reply(string? target, Callback? callback, string? failure = null)
    {
        var forUs = target is not null
            && (target == Callback.Path || target.StartsWith(Callback.Path + "?", StringComparison.Ordinal));
        string status;
        string? words;
        if (!forUs)
        {
            (status, words) = ("404 Not Found", null);
        }
        else if (callback?.Code is not null && failure is null)
        {
            (status, words) = ("200 OK", "Signed in to MaxNines. You can close this tab.");
        }
        else if (callback?.Code is not null)
        {
            (status, words) = (
                "200 OK",
                $"Not signed in to MaxNines: {WebUtility.HtmlEncode(failure)}. Start signing in again from the app.");
        }
        else if (callback?.Error is not null)
        {
            (status, words) = ("200 OK", "Not signed in to MaxNines. You can close this tab.");
        }
        else
        {
            (status, words) = ("400 Bad Request", "Not signed in to MaxNines. Start signing in again from the app.");
        }
        var page = words is null
            ? ""
            : "<!doctype html><html><head><meta charset=\"utf-8\"><title>MaxNines</title></head>"
                + "<body style=\"margin:0;height:100vh;display:grid;place-items:center;background:#0f1114;"
                + "color:#f3f5f4;font:16px system-ui,sans-serif\"><p>" + words + "</p></body></html>";
        return $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(page)}\r\nCache-Control: no-store\r\n"
            + $"Connection: close\r\n\r\n{page}";
    }
}
