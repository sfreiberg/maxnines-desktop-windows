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

    /// <summary>The key is wrong, revoked or expired, or its owner is disabled.</summary>
    NotSignedIn,

    /// <summary>The key's role doesn't allow this.</summary>
    NotAllowed,

    Status,

    /// <summary>Something answered, but not as a MaxNines server does.</summary>
    NotMaxNines,

    /// <summary>Nothing answered.</summary>
    Unreachable,

    /// <summary>A plain http address outside the local network, where the key would
    /// cross the internet unencrypted.</summary>
    NeedsHttps,
}

public sealed class ClientException(ClientProblem problem, int status = 0, Exception? inner = null)
    : Exception(Describe(problem, status), inner)
{
    public ClientProblem Problem { get; } = problem;

    static string Describe(ClientProblem problem, int status) => problem switch
    {
        ClientProblem.BadAddress => "The server address isn't a web address",
        ClientProblem.NotSignedIn => "The server didn't accept the API key",
        ClientProblem.NotAllowed =>
            "This API key isn't allowed to do that. Acknowledging and silencing need a responder key",
        ClientProblem.Status => $"The server answered with status {status}",
        ClientProblem.NotMaxNines => "That address didn't answer like a MaxNines server",
        ClientProblem.NeedsHttps =>
            "A server outside your local network needs an https address, so the key isn't sent unencrypted",
        _ => "The server didn't answer",
    };
}

/// <summary>Talks to one MaxNines server's API as the owner of an API key.</summary>
public sealed class Client
{
    public Uri Server { get; }
    readonly string apiKey;
    readonly HttpClient http;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <param name="address">What someone types: the address they open MaxNines at.</param>
    public Client(string address, string apiKey, HttpMessageHandler? handler = null)
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
        Server = new Uri(url.GetLeftPart(UriPartial.Authority));
        this.apiKey = apiKey.Trim();
        http = handler is null ? Shared : new HttpClient(handler);
    }

    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };

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
    /// Whether the key crosses the network unencrypted to somewhere other than this
    /// computer. Windows doesn't stop that, so the app warns about it.
    /// </summary>
    public bool SendsKeyInTheClear =>
        Server.Scheme == Uri.UriSchemeHttp && !Server.IsLoopback;

    internal HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(Server, "api/v1/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    async Task<string> Send(HttpRequestMessage request, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel);
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
        using (response)
        {
            var status = (int)response.StatusCode;
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => throw new ClientException(ClientProblem.NotSignedIn),
                HttpStatusCode.Forbidden => throw new ClientException(ClientProblem.NotAllowed),
                _ when status is >= 200 and < 300 => await response.Content.ReadAsStringAsync(cancel),
                _ => throw new ClientException(ClientProblem.Status, status),
            };
        }
    }

    async Task<T> Get<T>(string path, CancellationToken cancel)
    {
        var text = await Send(Request(HttpMethod.Get, path), cancel);
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

    /// <summary>Says the key's owner is dealing with an alert.</summary>
    public Task Acknowledge(long alertId, CancellationToken cancel = default) =>
        Send(Request(HttpMethod.Post, $"alerts/{alertId}/acknowledge"), cancel);

    /// <summary>Keeps an alert's notifications back for a while.</summary>
    public Task Silence(ServerAlert alert, TimeSpan duration, CancellationToken cancel = default) =>
        Send(Request(HttpMethod.Post, "silences", SilenceBody(alert, duration)), cancel);

    /// <summary>
    /// A silence for one alert: its rule on its host. An alert about a group of hosts
    /// belongs to none, so only its rule is named.
    /// </summary>
    internal static string SilenceBody(ServerAlert alert, TimeSpan duration)
    {
        // In name order, as the Mac app sends it.
        var body = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["reason"] = "Silenced from the notification area",
            ["duration_secs"] = (long)duration.TotalSeconds,
        };
        if (alert.RuleId is { } rule)
        {
            body["rule_id"] = rule;
        }
        if (alert.HostId != "fleet")
        {
            body["filter"] = $"id={alert.HostId}";
        }
        return new JsonObject(body).ToJsonString();
    }
}
