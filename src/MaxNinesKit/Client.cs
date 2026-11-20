using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MaxNines.Kit;

public enum ClientProblem
{
    /// <summary>The address isn't one a server could be at.</summary>
    BadAddress,

    /// <summary>
    /// The sign-in was revoked or ran out, or its owner is disabled, so it has to sign
    /// in again.
    /// </summary>
    NotSignedIn,

    /// <summary>The role it was signed in with doesn't allow this.</summary>
    NotAllowed,

    Status,

    /// <summary>Something answered, but not as a MaxNines server does.</summary>
    NotMaxNines,

    /// <summary>Nothing answered.</summary>
    Unreachable,

    /// <summary>
    /// Something answered, but a secure connection couldn't be made with it, most often
    /// as its certificate isn't trusted, has run out, or is for another name.
    /// </summary>
    Insecure,

    /// <summary>A plain http address outside the local network, where sign-ins would
    /// cross the internet unencrypted.</summary>
    NeedsHttps,

    /// <summary>The alert's rule has been deleted, so a silence can't name it.</summary>
    CantSilence,

    /// <summary>Someone chose Deny in the browser.</summary>
    SignInDenied,

    /// <summary>Nobody answered in the browser in time.</summary>
    SignInTimedOut,

    /// <summary>The server turned signing in down, for the reason it gave.</summary>
    SignInFailed,

    /// <summary>
    /// Signing in was cancelled after the server had signed the app in, and the server
    /// couldn't be told to undo it.
    /// </summary>
    SignInNotUndone,
}

public sealed class ClientException(
    ClientProblem problem, int status = 0, Exception? inner = null, string? detail = null)
    : Exception(Describe(problem, status, detail), inner)
{
    public ClientProblem Problem { get; } = problem;

    static string Describe(ClientProblem problem, int status, string? detail) => problem switch
    {
        ClientProblem.BadAddress => "The server address isn't a web address",
        ClientProblem.NotSignedIn => "Signed out: sign in again",
        ClientProblem.NotAllowed =>
            "Your role doesn't allow that. Acknowledging and silencing need the responder role",
        ClientProblem.Status => $"The server answered with status {status}",
        ClientProblem.NotMaxNines => "That address didn't answer like a MaxNines server",
        ClientProblem.Insecure when detail is not null => $"Couldn't make a secure connection to the server: {detail}",
        ClientProblem.Insecure => "Couldn't make a secure connection to the server",
        ClientProblem.NeedsHttps =>
            "A server outside your local network needs an https address, so signing in isn't sent unencrypted",
        ClientProblem.CantSilence => "This alert's rule has been deleted, so it can't be silenced from here",
        ClientProblem.SignInDenied => "Signing in was denied in the browser",
        ClientProblem.SignInTimedOut => "Signing in took too long. Try again",
        ClientProblem.SignInFailed when detail is not null => $"The server wouldn't sign the app in: {detail}",
        ClientProblem.SignInFailed => "The server wouldn't sign the app in",
        ClientProblem.SignInNotUndone =>
            "Signing in was cancelled, but the server had already signed the app in and couldn't be told to undo it. "
            + "Revoke it in MaxNines under Settings, Signed-in apps",
        _ => "The server didn't answer",
    };

    /// <summary>
    /// Makes a request with <paramref name="send"/>, saying one that got no answer as
    /// that: one that couldn't be made secure, or ran out of time, whether waiting for
    /// the answer or reading it. Only <paramref name="cancel"/> leaves it cancelled.
    /// </summary>
    internal static async Task<T> Asking<T>(Func<Task<T>> send, CancellationToken cancel)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException error)
        {
            throw Unanswered(error);
        }
        catch (OperationCanceledException error) when (!cancel.IsCancellationRequested)
        {
            // Timed out.
            throw new ClientException(ClientProblem.Unreachable, inner: error);
        }
    }

    /// <summary>
    /// A request that got no answer: one that couldn't be made secure, as when the
    /// server's certificate isn't trusted, said as that, and otherwise unreachable.
    /// </summary>
    static ClientException Unanswered(HttpRequestException error) =>
        error.HttpRequestError == HttpRequestError.SecureConnectionError
            // The inner exception says what was wrong, such as the certificate chain
            // ending in a root this computer doesn't trust.
            ? new ClientException(
                ClientProblem.Insecure, inner: error, detail: (error.InnerException ?? error).Message.TrimEnd('.'))
            : new ClientException(ClientProblem.Unreachable, inner: error);
}

/// <summary>Talks to one MaxNines server's API as whoever signed the app in.</summary>
public sealed class Client
{
    public Uri Server => signIn.Server;
    readonly AppSignIn signIn;
    readonly HttpClient http;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Client(AppSignIn signIn, HttpMessageHandler? handler = null)
    {
        this.signIn = signIn;
        http = handler is null ? Shared : new HttpClient(handler);
    }

    internal static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// The server's root, from what someone types: the address they open MaxNines at,
    /// or any page of it.
    /// </summary>
    public static Uri SiteRoot(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(url.Host))
        {
            throw new ClientException(ClientProblem.BadAddress);
        }
        // As macOS does for the Mac app: plain http only on the local network.
        if (url.Scheme == Uri.UriSchemeHttp && !IsLocalNetwork(url))
        {
            throw new ClientException(ClientProblem.NeedsHttps);
        }
        // Whatever page of the site was pasted, the API is at its root.
        return new Uri(url.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>
    /// This computer, a private or link-local address, or a name that only means
    /// something nearby: one with no dots, or ending in <c>.local</c>.
    /// </summary>
    internal static bool IsLocalNetwork(Uri url)
    {
        if (url.IsLoopback)
        {
            return true;
        }
        // Without the brackets an IPv6 address is written with in a URL.
        if (!IPAddress.TryParse(url.DnsSafeHost, out var address))
        {
            var host = url.IdnHost.TrimEnd('.');
            return !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // Unique local (fc00::/7) and link-local addresses.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        }
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and < 32)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    /// <summary>A page of the web interface, such as <c>/alerts</c>.</summary>
    public Uri Page(string path) => new(Server, path);

    /// <summary>
    /// Whether signing in and the tokens cross the network unencrypted to somewhere other
    /// than this computer. Windows doesn't stop that, so the app warns about it.
    /// </summary>
    public static bool SendsTokensInTheClear(Uri server) =>
        server.Scheme == Uri.UriSchemeHttp && !server.IsLoopback;

    internal HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(Server, "api/v1/" + path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    /// <summary>
    /// Sends a request with the access token. If the server turns the token away, which
    /// it does once it's run out or been revoked, it's sent once more with a new one.
    /// If it turns that away too, the app is signed out, as the sign-in is no use.
    /// </summary>
    async Task<string> Send(HttpMethod method, string path, string? body, CancellationToken cancel)
    {
        var token = await signIn.AccessToken(cancel);
        var (status, text) = await SendWith(token, method, path, body, cancel);
        if (status == (int)HttpStatusCode.Unauthorized)
        {
            token = await signIn.Rejected(token, cancel);
            (status, text) = await SendWith(token, method, path, body, cancel);
            if (status == (int)HttpStatusCode.Unauthorized && !await signIn.TurnedAway(token))
            {
                // A newer token came while this was sent, which the next request uses.
                throw new ClientException(ClientProblem.Status, status);
            }
        }
        return status switch
        {
            (int)HttpStatusCode.Unauthorized => throw new ClientException(ClientProblem.NotSignedIn),
            (int)HttpStatusCode.Forbidden => throw new ClientException(ClientProblem.NotAllowed),
            >= 200 and < 300 => text,
            _ => throw new ClientException(ClientProblem.Status, status),
        };
    }

    /// <summary>The status, and the body if it worked.</summary>
    Task<(int Status, string Text)> SendWith(
        string token, HttpMethod method, string path, string? body, CancellationToken cancel) =>
        ClientException.Asking(
            async () =>
            {
                using var request = Request(method, path, body);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request, cancel);
                var status = (int)response.StatusCode;
                return (status, status is >= 200 and < 300 ? await response.Content.ReadAsStringAsync(cancel) : "");
            },
            cancel);

    async Task<T> Get<T>(string path, CancellationToken cancel)
    {
        var text = await Send(HttpMethod.Get, path, null, cancel);
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? throw new JsonException("empty");
        }
        catch (JsonException error)
        {
            throw new ClientException(ClientProblem.NotMaxNines, inner: error);
        }
    }

    public Task<Alerts> GetAlerts(CancellationToken cancel = default) => Get<Alerts>("alerts", cancel);

    public Task<List<MonitoredHost>> GetHosts(CancellationToken cancel = default) =>
        Get<List<MonitoredHost>>("hosts", cancel);

    public Task<Silences> GetSilences(CancellationToken cancel = default) => Get<Silences>("silences", cancel);

    public Task<List<RemoteCheck>> GetRemoteChecks(CancellationToken cancel = default) =>
        Get<List<RemoteCheck>>("remote-checks", cancel);

    /// <summary>Says whoever signed the app in is dealing with an alert.</summary>
    public Task Acknowledge(long alertId, CancellationToken cancel = default) =>
        Send(HttpMethod.Post, $"alerts/{alertId}/acknowledge", null, cancel);

    /// <summary>Keeps an alert's notifications back for a while.</summary>
    public async Task Silence(ServerAlert alert, TimeSpan duration, CancellationToken cancel = default) =>
        await Send(HttpMethod.Post, "silences", SilenceBody(alert, duration), cancel);

    /// <summary>
    /// A silence for this alert and nothing else: its rule, on its host, for its
    /// series. An alert about a group of hosts belongs to no one host, and a filter
    /// would leave it out, so it has none.
    /// </summary>
    internal static string SilenceBody(ServerAlert alert, TimeSpan duration)
    {
        // Without a rule the silence would cover every rule.
        if (alert.RuleId is not { } rule)
        {
            throw new ClientException(ClientProblem.CantSilence);
        }
        // In name order, as the Mac app sends it.
        var body = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["reason"] = "Silenced from the notification area",
            ["duration_secs"] = (long)duration.TotalSeconds,
            ["rule_id"] = rule,
        };
        if (alert.HostId != "fleet")
        {
            body["filter"] = $"id={alert.HostId}";
        }
        if (alert.Series is { } series)
        {
            body["series"] = series;
        }
        return new JsonObject(body).ToJsonString();
    }
}
