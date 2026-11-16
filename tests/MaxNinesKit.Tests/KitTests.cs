using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MaxNines.Kit;
using Xunit;

namespace MaxNines.Kit.Tests;

public class KitTests
{
    /// <summary>As the server's API documentation shows them.</summary>
    const string AlertsJson = """
        {
          "open": [
            {"id": 7, "rule_id": 1, "rule_name": "High CPU", "severity": "warning",
             "host_id": "22ad9264b6eb708ffdbedcd5cc073e5c", "hostname": "web-1", "series": "",
             "value": 97.2, "summary": "High CPU on web-1", "started_ms": 1793940279545,
             "resolved_ms": null, "resolve_reason": null, "acknowledged_by": null,
             "acknowledged_ms": null, "silence_id": null, "maintenance_window_id": null},
            {"id": 9, "rule_id": null, "rule_name": "Service stopped", "severity": "critical",
             "host_id": "fleet", "hostname": "Fleet", "series": "nginx.service", "value": 0,
             "summary": "Service nginx.service on web-1 is failed", "started_ms": 1793940200000,
             "resolved_ms": null, "resolve_reason": null, "acknowledged_by": "sam@example.com",
             "acknowledged_ms": 1793940210000, "silence_id": 3, "maintenance_window_id": null,
             "added_later": {"anything": true}},
            {"id": 10, "rule_id": 4, "rule_name": "New", "severity": "catastrophic",
             "host_id": "a", "hostname": "db-1", "series": "", "value": null,
             "summary": "Something new", "started_ms": 1793940100000, "resolved_ms": null,
             "resolve_reason": null, "acknowledged_by": null, "acknowledged_ms": null,
             "silence_id": null, "maintenance_window_id": null}
          ],
          "resolved": [
            {"id": 5, "rule_id": 2, "rule_name": "Container down", "severity": "critical",
             "host_id": "a", "hostname": "db-1", "series": "web", "value": 0,
             "summary": "Container web on db-1 is exited", "started_ms": 1793936000000,
             "resolved_ms": 1793939000000, "resolve_reason": "The problem cleared",
             "acknowledged_by": null, "acknowledged_ms": null, "silence_id": null,
             "maintenance_window_id": null},
            {"id": 2, "rule_id": 2, "rule_name": "Container down", "severity": "critical",
             "host_id": "a", "hostname": "db-1", "series": "web", "value": 0,
             "summary": "Long ago", "started_ms": 1793000000000, "resolved_ms": 1793000100000,
             "resolve_reason": "The problem cleared", "acknowledged_by": null,
             "acknowledged_ms": null, "silence_id": null, "maintenance_window_id": null}
          ]
        }
        """;

    const string HostsJson = """
        [
          {"id": "22ad", "public_key": "912e", "hostname": "web-1", "state": "approved",
           "decided_by": "you@example.com", "first_seen_ms": 1, "decided_ms": 2, "online": true,
           "health": "online", "last_seen_ms": 3},
          {"id": "33be", "public_key": "a1b2", "hostname": "db-1", "state": "approved",
           "online": false, "health": "offline"},
          {"id": "44cf", "public_key": "c3d4", "hostname": "new", "state": "pending", "online": false},
          {"id": "server", "public_key": "", "hostname": "MaxNines server", "state": "approved",
           "online": false},
          {"id": "fleet", "public_key": "", "hostname": "Fleet", "state": "approved", "online": false},
          {"id": "55d0", "kind": "agentless", "public_key": "", "hostname": "orders-db",
           "state": "approved", "online": true, "health": "online", "last_seen_ms": 3},
          {"id": "66e1", "kind": "agentless", "public_key": "", "hostname": "not-set-up-yet",
           "state": "approved", "online": false, "health": "not_read", "last_seen_ms": null},
          {"id": "77f2", "kind": "built_in", "public_key": "", "hostname": "MaxNines server",
           "state": "approved", "online": false, "health": "offline"}
        ]
        """;

    const string ChecksJson = """
        [
          {"id": 1, "host_id": "server", "check": {"name": "Home page", "kind": "http", "target": "https://example.com"},
           "interval_secs": 60, "enabled": true, "read_stats": false, "sign_in": "none",
           "has_password": false, "has_private_key": false,
           "latest": {"up": true, "ran_ms": 1793940279545}},
          {"id": 2, "host_id": "55d0", "check": {"name": "db", "kind": "postgres", "target": "db:5432"},
           "interval_secs": 60, "enabled": true, "read_stats": true, "extras": ["sizes"], "sign_in": "password",
           "username": "maxnines", "has_password": true, "has_private_key": false,
           "latest": {"up": false, "ran_ms": 1793940279545, "error": "connection refused"}},
          {"id": 3, "host_id": "server", "check": {"name": "New", "kind": "tcp", "target": "a:1"},
           "interval_secs": 60, "enabled": true, "read_stats": false, "sign_in": "none",
           "has_password": false, "has_private_key": false, "latest": null},
          {"id": 4, "host_id": "server", "check": {"name": "Paused", "kind": "tcp", "target": "b:1"},
           "interval_secs": 60, "enabled": false, "read_stats": false, "sign_in": "none",
           "has_password": false, "has_private_key": false, "latest": {"up": false, "ran_ms": 1}}
        ]
        """;

    const string SilencesJson = """
        {
          "active": [
            {"id": 3, "rule_id": null, "filter": "hostname=db*", "series": "", "reason": "Upgrading Postgres",
             "created_by": "you@example.com", "starts_ms": 1793940000000, "ends_ms": 1793947479545},
            {"id": 4, "rule_id": 1, "filter": "id=22ad", "series": "", "reason": "Known",
             "created_by": "you@example.com", "starts_ms": 1793940000000, "ends_ms": 1793941800000},
            {"id": 5, "rule_id": 99, "filter": "", "series": "nginx.service", "reason": "Gone rule",
             "created_by": "you@example.com", "starts_ms": 1793940000000, "ends_ms": 1793950000000},
            {"id": 6, "rule_id": null, "filter": "", "series": "", "reason": "Everything",
             "created_by": "you@example.com", "starts_ms": 1793940000000, "ends_ms": 1793960000000}
          ],
          "ended": [{"id": 1, "rule_id": null, "filter": "", "series": "", "reason": "Old",
                     "created_by": "you@example.com", "starts_ms": 1, "ends_ms": 2}]
        }
        """;

    /// <summary>Just after the alerts above.</summary>
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_793_940_300);

    static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Client.Json)!;

    static (Alerts, List<MonitoredHost>) Decoded() => (Read<Alerts>(AlertsJson), Read<List<MonitoredHost>>(HostsJson));

    [Fact]
    public void AlertsAreReadAsTheServerSendsThem()
    {
        var (alerts, _) = Decoded();
        var first = alerts.Open[0];
        Assert.Equal(7, first.Id);
        Assert.Equal(1, first.RuleId);
        Assert.Equal(Severity.Warning, first.Severity);
        Assert.Equal("web-1", first.Hostname);
        Assert.False(first.IsAcknowledged || first.IsSilenced);

        var second = alerts.Open[1];
        Assert.Null(second.RuleId);
        Assert.Equal(Severity.Critical, second.Severity);
        Assert.True(second.IsAcknowledged && second.IsSilenced);
        // A severity this app has never heard of still shows.
        Assert.Equal(Severity.Warning, alerts.Open[2].Severity);
    }

    [Fact]
    public void TheOverviewPutsTheWorstFirstAndCountsRealHosts()
    {
        var (alerts, hosts) = Decoded();
        var overview = new Overview(alerts, hosts, now: Now);

        Assert.Equal([9, 7, 10], overview.Firing.Select(alert => alert.Id));
        Assert.Equal(Severity.Critical, overview.Worst);
        Assert.Equal("1 critical, 2 warnings", overview.Headline);
        // Only what ended in the last hour.
        Assert.Equal([5], overview.Resolved.Select(alert => alert.Id));
        // Approved hosts, with an agent or without, but not ones waiting, the server's
        // built-in hosts, or one with nothing read for it, which couldn't be online.
        Assert.Equal(3, overview.Hosts);
        Assert.Equal(2, overview.HostsOnline);
    }

    [Fact]
    public void NothingFiringSaysSo()
    {
        var overview = new Overview(new Alerts(), [], now: Now);
        Assert.Equal("Nothing firing", overview.Headline);
        Assert.Null(overview.Worst);
    }

    [Fact]
    public void AgesUseTheLargestWholeUnit()
    {
        string Ago(long seconds) => Times.Age(Now.ToUnixTimeMilliseconds() - seconds * 1000, Now);
        Assert.Equal("0s", Ago(0));
        Assert.Equal("59s", Ago(59));
        Assert.Equal("1m", Ago(60));
        Assert.Equal("59m", Ago(3599));
        Assert.Equal("2h", Ago(7200));
        Assert.Equal("2d", Ago(200_000));
        // A clock that's a little out doesn't make an age negative.
        Assert.Equal("0s", Ago(-5));
    }

    [Fact]
    public void TimeLeftRoundsUpAndNeverGoesNegative()
    {
        string Left(long seconds) => Times.Left(Now.ToUnixTimeMilliseconds() + seconds * 1000, Now);
        Assert.Equal("25m", Left(25 * 60));
        Assert.Equal("1m", Left(90));
        Assert.Equal("3h", Left(3 * 3600));
        Assert.Equal("0s", Left(-10));
        Assert.Equal("1s", Times.Left(Now.ToUnixTimeMilliseconds() + 1, Now));
    }

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Signed in just now, with tokens that work for an hour.</summary>
    static AppSignIn SignedIn(string address, HttpMessageHandler handler, Remembered? store = null, Clock? clock = null) =>
        new(new OAuth(address, handler), store ?? new Remembered(), Tokens("mxa_0", "mxr_0"), clock, retryDelays: []);

    static TokenResponse Tokens(string access, string refresh) => new()
    {
        AccessToken = access,
        ExpiresIn = 3600,
        RefreshToken = refresh,
        Role = "responder",
        User = "sam@example.com",
    };

    [Fact]
    public async Task TheServerIsFoundFromWhateverPageWasPasted()
    {
        Assert.Equal("http://192.168.0.155:9000/", Client.SiteRoot(" http://192.168.0.155:9000/alerts?x=1#top ").ToString());
        Assert.True(Client.SendsTokensInTheClear(Client.SiteRoot("http://192.168.0.155:9000")));
        Assert.False(Client.SendsTokensInTheClear(Client.SiteRoot("https://monitor.example.com")));
        Assert.False(Client.SendsTokensInTheClear(Client.SiteRoot("http://localhost:9000")));

        var server = new Fake((request, _) => Task.FromResult(Reply(200, HostsJson)));
        var client = new Client(SignedIn("http://192.168.0.155:9000/alerts?x=1#top", server), server);
        Assert.Equal("http://192.168.0.155:9000/hosts", client.Page("/hosts").ToString());
        await client.Acknowledge(7, Cancel);
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST http://192.168.0.155:9000/api/v1/alerts/7/acknowledge Bearer mxa_0", request);

        foreach (var bad in new[] { "", "monitor.example.com", "ftp://example.com", "http://" })
        {
            var refused = Assert.Throws<ClientException>(() => Client.SiteRoot(bad));
            Assert.Equal(ClientProblem.BadAddress, refused.Problem);
            Assert.Equal(ClientProblem.BadAddress, Assert.Throws<ClientException>(() => new OAuth(bad)).Problem);
        }
    }

    [Fact]
    public void PlainHttpIsOnlyForTheLocalNetwork()
    {
        foreach (var local in new[]
        {
            "http://localhost:9000", "http://127.0.0.1:9000", "http://[::1]:9000", "http://10.1.2.3",
            "http://172.16.0.1", "http://172.31.255.255", "http://192.168.0.155:9000", "http://169.254.1.1",
            "http://[fd00::1]", "http://[fe80::1]", "http://monitor", "http://monitor.local:9000",
        })
        {
            Assert.Equal(local, Client.SiteRoot(local).GetLeftPart(UriPartial.Authority));
        }
        foreach (var far in new[] { "http://monitor.example.com", "http://8.8.8.8", "http://172.32.0.1", "http://[2001:db8::1]" })
        {
            var refused = Assert.Throws<ClientException>(() => Client.SiteRoot(far));
            Assert.Equal(ClientProblem.NeedsHttps, refused.Problem);
            // Over https it's fine.
            Assert.False(Client.SendsTokensInTheClear(Client.SiteRoot(far.Replace("http:", "https:"))));
        }
    }

    [Fact]
    public async Task AnswersBecomeProblemsThatSayWhatWentWrong()
    {
        async Task<ClientProblem> Answered(int status, string body = "[]")
        {
            // The server turns refreshing away too, as it does once the sign-in's revoked.
            var server = new Fake((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/oauth/token"
                ? Reply(400, """{"error": "invalid_grant", "error_description": "Sign in again"}""")
                : Reply(status, body)));
            var client = new Client(SignedIn("https://monitor.example.com", server), server);
            var refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(Cancel));
            return refused.Problem;
        }
        Assert.Equal(ClientProblem.NotSignedIn, await Answered(401));
        Assert.Equal(ClientProblem.NotAllowed, await Answered(403));
        Assert.Equal(ClientProblem.Status, await Answered(500));
        Assert.Equal(ClientProblem.NotMaxNines, await Answered(200, "<html>"));
        var fine = new Fake((_, _) => Task.FromResult(Reply(200, HostsJson)));
        Assert.Equal(8, (await new Client(SignedIn("https://monitor.example.com", fine), fine).GetHosts(Cancel)).Count);
    }

    /// <summary>A server that answers however it's told to, noting each request.</summary>
    sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        readonly List<string> requests = [];

        /// <summary>Each as its method, address and token.</summary>
        public List<string> Requests
        {
            get
            {
                lock (requests)
                {
                    return [.. requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            lock (requests)
            {
                requests.Add($"{request.Method} {request.RequestUri} {request.Headers.Authorization}".TrimEnd());
            }
            return answer(request, cancel);
        }
    }

    static HttpResponseMessage Reply(int status, string body) =>
        new((System.Net.HttpStatusCode)status) { Content = new StringContent(body) };

    // Checks and silences

    [Fact]
    public void ChecksCountOnlyWhatsRunningAndHasRun()
    {
        var (alerts, hosts) = Decoded();
        var checks = Read<List<RemoteCheck>>(ChecksJson);
        var overview = new Overview(alerts, hosts, checks, now: Now);
        // The paused one isn't counted, and the one that hasn't run can't pass or fail.
        Assert.Equal(new CheckCount(1, 2, 3), overview.Checks);
        Assert.Equal(1, overview.Checks!.Failing);

        // A server with none, or one that couldn't say, has nothing to show.
        Assert.Null(new Overview(alerts, hosts, [], now: Now).Checks);
        Assert.Null(new Overview(alerts, hosts, null, now: Now).Checks);
        Assert.Null(new Overview(alerts, hosts, checks.Where(check => !check.Enabled).ToList(), now: Now).Checks);
    }

    [Fact]
    public void ChecksFromAServerTooOldToSayHaveNotRun()
    {
        var checks = Read<List<RemoteCheck>>("""
            [{"id": 1, "host_id": "server", "check": {"name": "a", "kind": "tcp", "target": "a:1"},
              "interval_secs": 60, "enabled": true}]
            """);
        var (alerts, hosts) = Decoded();
        Assert.Equal(0, new Overview(alerts, hosts, checks, now: Now).Checks!.Run);
    }

    [Fact]
    public void SilencesSayWhatTheyCoverAndEndSoonestFirst()
    {
        var (alerts, hosts) = Decoded();
        var silences = Read<Silences>(SilencesJson).Active;
        var overview = new Overview(alerts, hosts, silences: silences, now: Now);
        Assert.Equal([4, 3, 5, 6], overview.Silences.Select(silence => silence.Id));
        Assert.Equal(
            [
                // A rule named by one of its alerts, on a host named by its ID.
                "High CPU on web-1",
                "Everything on hostname=db*",
                // A rule with no alert to name it.
                "One alert rule nginx.service on every host",
                "Every alert",
            ],
            overview.Silences.Select(silence => silence.Covers));
        Assert.Equal("Known", overview.Silences[0].Reason);
    }

    // Notifications

    static ServerAlert Alert(
        long id, Severity severity, bool silenced = false, bool acknowledged = false, long? resolvedMs = null) =>
        new()
        {
            Id = id,
            RuleId = 1,
            RuleName = $"Rule {id}",
            Severity = severity,
            HostId = $"h{id}",
            Hostname = $"host-{id}",
            Summary = $"Alert {id}",
            StartedMs = 1793940000000,
            ResolvedMs = resolvedMs,
            AcknowledgedBy = acknowledged ? "sam@example.com" : null,
            SilenceId = silenced ? 1 : null,
        };

    static readonly NotifyWhen Everything = new(true, true, true);

    [Fact]
    public void NewAlertsAreNotifiedAsWanted()
    {
        var old = new Alerts { Open = [Alert(1, Severity.Critical)] };
        var current = new Alerts
        {
            Open =
            [
                Alert(1, Severity.Critical), Alert(2, Severity.Critical), Alert(3, Severity.Warning),
                Alert(4, Severity.Critical, silenced: true),
            ],
        };
        var all = Notices.Between(old, current, Everything, Now);
        // Not the one already firing, nor the silenced one.
        Assert.Equal(["Critical: Rule 2", "Warning: Rule 3"], all.Select(notice => notice.Title));
        Assert.Equal("Alert 2", all[0].Body);
        Assert.Equal(2, all[0].Acknowledges);
        Assert.Equal("/hosts/h2", all[0].Path);

        var criticalOnly = new NotifyWhen(true, false, false);
        Assert.Equal(["Critical: Rule 2"], Notices.Between(old, current, criticalOnly, Now).Select(notice => notice.Title));
        Assert.Empty(Notices.Between(old, current, new NotifyWhen(false, false, false), Now));
    }

    [Fact]
    public void AnAlertThatGetsWorseIsNotified()
    {
        var old = new Alerts { Open = [Alert(1, Severity.Warning)] };
        var current = new Alerts { Open = [Alert(1, Severity.Critical, acknowledged: true)] };
        var all = Notices.Between(old, current, Everything, Now);
        Assert.Equal(["Now critical: Rule 1"], all.Select(notice => notice.Title));
        // Someone's already on it.
        Assert.Null(all[0].Acknowledges);
        // Getting better isn't news.
        Assert.Empty(Notices.Between(current, old, Everything, Now));
    }

    [Fact]
    public void AnAlertStillFiringWhenItsSilenceEndsIsNotified()
    {
        var old = new Alerts
        {
            Open = [Alert(1, Severity.Critical, silenced: true), Alert(2, Severity.Warning, silenced: true)],
        };
        var current = new Alerts { Open = [Alert(1, Severity.Critical), Alert(2, Severity.Critical)] };
        // As the server tells people then, and as what it is now, not how it changed.
        Assert.Equal(
            ["Critical: Rule 1", "Critical: Rule 2"],
            Notices.Between(old, current, Everything).Select(notice => notice.Title));
        // Still silenced, it stays quiet.
        Assert.Empty(Notices.Between(old, old, Everything));
    }

    [Fact]
    public void ResolvedAlertsSayHowLongTheyLasted()
    {
        var old = new Alerts
        {
            Open = [Alert(1, Severity.Critical), Alert(2, Severity.Warning), Alert(3, Severity.Critical, silenced: true)],
        };
        var current = new Alerts
        {
            Resolved =
            [
                Alert(1, Severity.Critical, resolvedMs: 1793940180000),
                Alert(3, Severity.Critical, resolvedMs: 1793940180000),
            ],
        };
        var all = Notices.Between(old, current, Everything, Now);
        // The second just went, which the server doesn't call resolved, and the third
        // was silenced.
        Assert.Equal(["Resolved: Rule 1"], all.Select(notice => notice.Title));
        Assert.Equal("Alert 1\nLasted 3m", all[0].Body);
        Assert.Null(all[0].Acknowledges);
        Assert.Empty(Notices.Between(old, current, new NotifyWhen(true, true, false), Now));
    }

    [Fact]
    public void AStormBecomesOneNotification()
    {
        var old = new Alerts();
        var current = new Alerts { Open = Enumerable.Range(1, 5).Select(id => Alert(id, Severity.Critical)).ToList() };
        var all = Notices.Between(old, current, Everything, Now);
        var only = Assert.Single(all);
        Assert.Equal("5 alerts started", only.Title);
        Assert.Equal("Alert 1\nAlert 2\nAlert 3\nand 2 more", only.Body);
        Assert.Equal("/alerts", only.Path);
        Assert.Null(only.Acknowledges);
        // Three at once are each shown.
        var three = new Alerts { Open = Enumerable.Range(1, 3).Select(id => Alert(id, Severity.Critical)).ToList() };
        Assert.Equal(3, Notices.Between(old, three, Everything, Now).Count);
    }
    // Signing in

    /// <summary>Keeps the refresh token in memory, as Credential Manager would.</summary>
    sealed class Remembered : IRefreshTokenStore
    {
        public string? Token;
        public int Saves;
        public bool Broken;

        public void Save(string? refreshToken)
        {
            if (Broken)
            {
                throw new IOException("The vault is locked");
            }
            Token = refreshToken;
            Saves++;
        }
    }

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_793_940_300);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// A server that only takes its newest tokens, as the real one does, and swaps
    /// refresh tokens slowly enough for requests to pile up behind a swap.
    /// </summary>
    sealed class TokenServer : HttpMessageHandler
    {
        readonly object gate = new();
        int swaps;
        public string Access = "mxa_0";
        public string Refresh = "mxr_0";
        public bool Unreachable;

        /// <summary>How many refreshes to fail on the way before answering one.</summary>
        public int Lose;

        /// <summary>Each refresh form it was sent.</summary>
        public readonly List<string> Swaps = [];

        /// <summary>How many times it was asked to swap a refresh token.</summary>
        public int Refreshes;

        /// <summary>How many requests it turned away for their access token.</summary>
        public int Rejections;

        public readonly List<string> Revoked = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel);
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/oauth/token":
                    Interlocked.Increment(ref Refreshes);
                    await Task.Delay(50, cancel);
                    if (Unreachable)
                    {
                        throw new HttpRequestException("No route to host");
                    }
                    lock (gate)
                    {
                        Swaps.Add(form);
                        if (Lose > 0)
                        {
                            Lose--;
                            throw new HttpRequestException("The connection was reset");
                        }
                        if (form != $"grant_type=refresh_token&client_id=maxnines-tray&refresh_token={Refresh}")
                        {
                            return Reply(400, """{"error": "invalid_grant", "error_description": "Sign in again"}""");
                        }
                        swaps++;
                        (Access, Refresh) = ($"mxa_{swaps}", $"mxr_{swaps}");
                        return Reply(200, $$"""
                            {"access_token": "{{Access}}", "token_type": "Bearer", "expires_in": 3600,
                             "refresh_token": "{{Refresh}}", "refresh_token_expires_in": 7776000,
                             "role": "viewer", "user": "sam@example.com", "sign_in_id": 4}
                            """);
                    }
                case "/oauth/revoke":
                    lock (gate)
                    {
                        Revoked.Add(form);
                    }
                    return Reply(200, "");
                default:
                    lock (gate)
                    {
                        if (request.Headers.Authorization?.Parameter == Access)
                        {
                            return Reply(200, "[]");
                        }
                        Rejections++;
                        return Reply(401, "");
                    }
            }
        }
    }

    /// <summary>
    /// Started again, with only the refresh token kept from before. A failed refresh
    /// isn't tried again unless <paramref name="retryDelays"/> says so.
    /// </summary>
    static AppSignIn Resumed(TokenServer server, Remembered store, TimeSpan[]? retryDelays = null) =>
        new(new OAuth("https://monitor.example.com", server), store, "mxr_0", "sam@example.com", "responder",
            retryDelays: retryDelays ?? []);

    static Dictionary<string, string> Fields(string query) =>
        query.TrimStart('?').Split('&')
            .Select(field => field.Split('=', 2))
            .ToDictionary(field => field[0], field => Uri.UnescapeDataString(field[1].Replace('+', ' ')));

    [Fact]
    public void TheChallengeIsTheVerifiersSha256()
    {
        // RFC 7636's own example.
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var verifier = Pkce.NewVerifier();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier);
        Assert.NotEqual(verifier, Pkce.NewVerifier());
        Assert.Matches("^[A-Za-z0-9_-]{22}$", Pkce.NewState());
    }

    [Fact]
    public void TheBrowserIsSentToTheApprovalPageWithEverythingItNeeds()
    {
        var oauth = new OAuth("https://monitor.example.com/alerts?x=1");
        Assert.Equal(
            "https://monitor.example.com/oauth/authorize?response_type=code&client_id=maxnines-tray"
                + "&redirect_uri=http%3A%2F%2F127.0.0.1%3A53682%2Fcallback"
                + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
                + "&state=af0ifjsldkj&device=sam%20laptop&role=responder",
            oauth.AuthorizeUrl(
                "http://127.0.0.1:53682/callback", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "af0ifjsldkj",
                "sam laptop", "responder"));
    }

    [Fact]
    public void TheAnswerIsReadFromWhereTheBrowserCameBack()
    {
        Assert.Equal(new Callback("mxc_a b", null), Callback.Parse("/callback?code=mxc_a%20b&state=s1", "s1"));
        Assert.Equal(new Callback(null, "access_denied"), Callback.Parse("/callback?error=access_denied&state=s1", "s1"));
        // Not an answer to this request, or not an answer at all.
        Assert.Null(Callback.Parse("/callback?code=mxc_a&state=other", "s1"));
        Assert.Null(Callback.Parse("/callback?code=mxc_a", "s1"));
        Assert.Null(Callback.Parse("/callback?state=s1", "s1"));
        Assert.Null(Callback.Parse("/elsewhere?code=mxc_a&state=s1", "s1"));
        Assert.Null(Callback.Parse("/favicon.ico", "s1"));
    }

    const string TokensJson = """
        {"access_token": "mxa_1", "token_type": "Bearer", "expires_in": 3600, "refresh_token": "mxr_1",
         "refresh_token_expires_in": 7776000, "role": "responder", "user": "you@example.com", "sign_in_id": 4}
        """;

    [Fact]
    public async Task SigningInThroughTheBrowserEndsWithTokens()
    {
        string? posted = null;
        var server = new Fake(async (request, cancel) =>
        {
            posted = await request.Content!.ReadAsStringAsync(cancel);
            return Reply(200, TokensJson);
        });
        var oauth = new OAuth("https://monitor.example.com", server);
        using var browser = new HttpClient();
        string? challenge = null;
        Task<string>? visiting = null;
        var tokens = await oauth.SignInWithBrowser(
            url =>
            {
                Assert.StartsWith("https://monitor.example.com/oauth/authorize?", url);
                var asked = Fields(new Uri(url).Query);
                Assert.Equal("code", asked["response_type"]);
                Assert.Equal("maxnines-tray", asked["client_id"]);
                Assert.Equal("S256", asked["code_challenge_method"]);
                Assert.Equal("test-pc", asked["device"]);
                Assert.Equal("responder", asked["role"]);
                Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", asked["redirect_uri"]);
                challenge = asked["code_challenge"];
                // As the server sends the browser back when they choose Allow.
                visiting = browser.GetStringAsync($"{asked["redirect_uri"]}?code=mxc_1&state={asked["state"]}", Cancel);
            },
            "test-pc",
            "responder",
            TimeSpan.FromMinutes(1),
            Cancel);

        Assert.Equal("mxa_1", tokens.AccessToken);
        Assert.Equal(3600, tokens.ExpiresIn);
        Assert.Equal("mxr_1", tokens.RefreshToken);
        Assert.Equal("responder", tokens.Role);
        Assert.Equal("you@example.com", tokens.User);
        Assert.Contains("Signed in to MaxNines. You can close this tab.", await visiting!);
        Assert.Equal(["POST https://monitor.example.com/oauth/token"], server.Requests);
        var form = Fields(posted!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("maxnines-tray", form["client_id"]);
        Assert.Equal("mxc_1", form["code"]);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", form["redirect_uri"]);
        // The verifier the challenge was made from.
        Assert.Equal(challenge, Pkce.Challenge(form["code_verifier"]));
    }

    [Fact]
    public async Task OAuthErrorsSayWhatWentWrong()
    {
        async Task<ClientException> Refused(int status, string body, bool refreshing)
        {
            var server = new Fake((_, _) => Task.FromResult(Reply(status, body)));
            var oauth = new OAuth("https://monitor.example.com", server);
            return await Assert.ThrowsAsync<ClientException>(() => refreshing
                ? oauth.Refresh("mxr_0", Cancel)
                : oauth.Redeem("mxc_1", "http://127.0.0.1:1/callback", "verifier", Cancel));
        }
        const string invalidGrant = """{"error": "invalid_grant", "error_description": "That code is wrong, used, or out of date"}""";
        var used = await Refused(400, invalidGrant, refreshing: false);
        Assert.Equal(ClientProblem.SignInFailed, used.Problem);
        Assert.Equal("The server wouldn't sign the app in: That code is wrong, used, or out of date", used.Message);
        // Refreshing, it means signing in again.
        var signedOut = await Refused(400, invalidGrant, refreshing: true);
        Assert.Equal(ClientProblem.NotSignedIn, signedOut.Problem);
        Assert.Equal("Signed out: sign in again", signedOut.Message);
        var unknown = await Refused(401, """{"error": "invalid_client", "error_description": "That isn't an app MaxNines knows"}""", false);
        Assert.Equal(ClientProblem.SignInFailed, unknown.Problem);
        Assert.Equal(ClientProblem.Status, (await Refused(500, "oops", refreshing: true)).Problem);
        Assert.Equal(ClientProblem.Status, (await Refused(404, "<html>", refreshing: false)).Problem);
        Assert.Equal(ClientProblem.NotMaxNines, (await Refused(200, "{}", refreshing: true)).Problem);
        Assert.Equal(ClientProblem.NotMaxNines, (await Refused(200, "<html>", refreshing: false)).Problem);
    }

    [Fact]
    public async Task OnlyOneRefreshRunsHoweverManyAskAtOnce()
    {
        var server = new TokenServer();
        var store = new Remembered();
        var signIn = Resumed(server, store);
        var client = new Client(signIn, server);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetHosts(Cancel)));
        Assert.Equal(1, server.Refreshes);
        Assert.Equal(0, server.Rejections);
        Assert.Equal("mxr_1", store.Token);
        Assert.Equal(1, store.Saves);
        // As the server said this time.
        Assert.Equal("viewer", signIn.Role);
        // Then it isn't needed again for a while.
        await client.GetHosts(Cancel);
        Assert.Equal(1, server.Refreshes);
    }

    [Fact]
    public async Task ATokenTurnedAwayIsRefreshedOnceAndTheRequestsTriedAgain()
    {
        // Revoked early, or the server restarted: it no longer takes the one held.
        var server = new TokenServer { Access = "mxa_elsewhere" };
        var store = new Remembered();
        var client = new Client(SignedIn("https://monitor.example.com", server, store), server);
        var hosts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetHosts(Cancel)));
        Assert.Equal(20, hosts.Length);
        Assert.Equal(1, server.Refreshes);
        Assert.True(server.Rejections >= 1);
        Assert.Equal("mxr_1", store.Token);
    }

    [Fact]
    public async Task ARefreshIsDueAMinuteBeforeTheAccessTokenRunsOut()
    {
        var server = new TokenServer();
        var clock = new Clock();
        var store = new Remembered();
        var client = new Client(SignedIn("https://monitor.example.com", server, store, clock), server);
        clock.Now += TimeSpan.FromMinutes(58);
        await client.GetHosts(Cancel);
        Assert.Equal(0, server.Refreshes);
        clock.Now += TimeSpan.FromSeconds(61);
        await client.GetHosts(Cancel);
        Assert.Equal(1, server.Refreshes);
        // Swapped before it ran out, not after the server turned it away.
        Assert.Equal(0, server.Rejections);
        Assert.Equal("mxr_1", store.Token);
    }

    [Fact]
    public async Task ARefreshTokenTheServerWontTakeSignsTheAppOut()
    {
        var server = new TokenServer { Refresh = "mxr_newer" };
        var store = new Remembered { Token = "mxr_0" };
        var signIn = Resumed(server, store);
        var client = new Client(signIn, server);
        var refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(Cancel));
        Assert.Equal(ClientProblem.NotSignedIn, refused.Problem);
        Assert.True(signIn.IsSignedOut);
        Assert.Null(store.Token);
        // Without asking the server again.
        refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(Cancel));
        Assert.Equal(ClientProblem.NotSignedIn, refused.Problem);
        Assert.Equal(1, server.Refreshes);
    }

    [Fact]
    public async Task ARefreshThatCantReachTheServerIsTriedAgainNextTime()
    {
        var server = new TokenServer { Unreachable = true };
        var store = new Remembered { Token = "mxr_0" };
        var signIn = Resumed(server, store);
        var client = new Client(signIn, server);
        var refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(Cancel));
        Assert.Equal(ClientProblem.Unreachable, refused.Problem);
        Assert.False(signIn.IsSignedOut);
        Assert.Equal(0, store.Saves);
        server.Unreachable = false;
        await client.GetHosts(Cancel);
        Assert.Equal(2, server.Refreshes);
        Assert.Equal("mxr_1", store.Token);
    }

    [Fact]
    public async Task ALostRefreshIsTriedAgainWithTheSameToken()
    {
        var server = new TokenServer { Lose = 2 };
        var store = new Remembered();
        var signIn = Resumed(server, store, [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        Assert.Equal("mxa_1", await signIn.AccessToken(Cancel));
        Assert.Equal(3, server.Refreshes);
        Assert.Equal(
            Enumerable.Repeat("grant_type=refresh_token&client_id=maxnines-tray&refresh_token=mxr_0", 3),
            server.Swaps);
        Assert.Equal(1, store.Saves);
        Assert.Equal("mxr_1", store.Token);
    }

    [Fact]
    public async Task ARefreshIsOnlyTriedAgainAsManyTimesAsItsDelays()
    {
        var server = new TokenServer { Unreachable = true };
        var store = new Remembered { Token = "mxr_0" };
        var signIn = Resumed(server, store, [TimeSpan.Zero, TimeSpan.Zero]);
        var refused = await Assert.ThrowsAsync<ClientException>(() => signIn.AccessToken(Cancel));
        Assert.Equal(ClientProblem.Unreachable, refused.Problem);
        Assert.Equal(3, server.Refreshes);
        Assert.Equal(0, store.Saves);
        Assert.False(signIn.IsSignedOut);
    }

    [Fact]
    public async Task SigningOutStopsARefreshBeingTriedAgain()
    {
        var server = new TokenServer { Unreachable = true };
        var store = new Remembered { Token = "mxr_0" };
        var signIn = Resumed(server, store, [TimeSpan.FromMinutes(10)]);
        var asking = signIn.AccessToken(Cancel);
        await signIn.SignOut(Cancel).WaitAsync(TimeSpan.FromSeconds(30), Cancel);
        await Assert.ThrowsAsync<ClientException>(() => asking);
        Assert.Equal(1, server.Refreshes);
        Assert.Equal(["client_id=maxnines-tray&token=mxr_0"], server.Revoked);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task ANewRefreshTokenThatCouldntBeKeptIsStillTheOneUsedNext()
    {
        var server = new TokenServer();
        var store = new Remembered { Broken = true };
        var client = new Client(Resumed(server, store), server);
        await Assert.ThrowsAsync<IOException>(() => client.GetHosts(Cancel));
        store.Broken = false;
        // Swapping the old one again would sign the app out.
        await client.GetHosts(Cancel);
        Assert.Equal(2, server.Refreshes);
        Assert.Equal("mxr_2", store.Token);
    }

    [Fact]
    public async Task SigningOutRevokesTheNewestTokenAndForgetsIt()
    {
        var server = new TokenServer();
        var store = new Remembered { Token = "mxr_0" };
        var signIn = Resumed(server, store);
        var client = new Client(signIn, server);
        // A refresh running is waited for, as only the token it brings can be revoked.
        var asking = client.GetHosts(Cancel);
        await signIn.SignOut(Cancel);
        await asking;
        Assert.Equal(["client_id=maxnines-tray&token=mxr_1"], server.Revoked);
        Assert.Null(store.Token);
        Assert.True(signIn.IsSignedOut);
        var refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(Cancel));
        Assert.Equal(ClientProblem.NotSignedIn, refused.Problem);
        Assert.Equal(1, server.Refreshes);
    }

    [Fact]
    public async Task SigningOutForgetsTheTokenEvenIfTheServerCantBeTold()
    {
        var server = new Fake((_, _) => throw new HttpRequestException("No route to host"));
        var store = new Remembered { Token = "mxr_0" };
        var signIn = SignedIn("https://monitor.example.com", server, store);
        var refused = await Assert.ThrowsAsync<ClientException>(() => signIn.SignOut(Cancel));
        Assert.Equal(ClientProblem.Unreachable, refused.Problem);
        Assert.Null(store.Token);
        Assert.True(signIn.IsSignedOut);
    }

    // Where the browser comes back to

    [Fact]
    public async Task TheBrowserComingBackIsAnsweredWithAPage()
    {
        using var loopback = new Loopback();
        Assert.Equal($"http://127.0.0.1:{loopback.Port}/callback", loopback.RedirectUri);
        var waiting = loopback.Wait("s1", TimeSpan.FromMinutes(1), Cancel);
        using var browser = new HttpClient();
        // A connection opened ahead of time and never used holds nothing up.
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, loopback.Port, Cancel);
        using var icon = await browser.GetAsync($"http://127.0.0.1:{loopback.Port}/favicon.ico", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, icon.StatusCode);
        // Someone else's answer is turned away, and it goes on waiting.
        using var stranger = await browser.GetAsync($"{loopback.RedirectUri}?code=mxc_theirs&state=wrong", Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Contains("Not signed in", await stranger.Content.ReadAsStringAsync(Cancel));
        Assert.False(waiting.IsCompleted);
        var page = await browser.GetStringAsync($"{loopback.RedirectUri}?code=mxc_1&state=s1", Cancel);
        Assert.Contains("Signed in to MaxNines. You can close this tab.", page);
        Assert.Equal(new Callback("mxc_1", null), await waiting.WaitAsync(TimeSpan.FromSeconds(30), Cancel));
    }

    [Fact]
    public async Task ADeniedSignInIsAnsweredToo()
    {
        using var loopback = new Loopback();
        var waiting = loopback.Wait("s1", TimeSpan.FromMinutes(1), Cancel);
        using var browser = new HttpClient();
        var page = await browser.GetStringAsync($"{loopback.RedirectUri}?error=access_denied&state=s1", Cancel);
        Assert.Contains("Not signed in to MaxNines. You can close this tab.", page);
        Assert.Equal(new Callback(null, "access_denied"), await waiting.WaitAsync(TimeSpan.FromSeconds(30), Cancel));
    }

    [Fact]
    public async Task WaitingForTheBrowserTimesOutOrIsCancelled()
    {
        using var loopback = new Loopback();
        var late = await Assert.ThrowsAsync<ClientException>(() => loopback.Wait("s1", TimeSpan.FromMilliseconds(50), Cancel));
        Assert.Equal(ClientProblem.SignInTimedOut, late.Problem);
        using var cancelling = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var waiting = loopback.Wait("s1", TimeSpan.FromMinutes(1), cancelling.Token);
        cancelling.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
