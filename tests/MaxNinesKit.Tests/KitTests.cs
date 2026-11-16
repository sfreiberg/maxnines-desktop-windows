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

    [Fact]
    public void TheServerIsFoundFromWhateverPageWasPasted()
    {
        var client = new Client(" http://192.168.0.155:9000/alerts?x=1#top ", " key\n");
        Assert.Equal("http://192.168.0.155:9000/", client.Server.ToString());
        Assert.Equal("http://192.168.0.155:9000/hosts", client.Page("/hosts").ToString());
        Assert.True(client.SendsKeyInTheClear);

        var request = client.Request(HttpMethod.Post, "alerts/7/acknowledge");
        Assert.Equal("http://192.168.0.155:9000/api/v1/alerts/7/acknowledge", request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer key", request.Headers.Authorization!.ToString());

        Assert.False(new Client("https://monitor.example.com", "k").SendsKeyInTheClear);
        Assert.False(new Client("http://localhost:9000", "k").SendsKeyInTheClear);
        foreach (var bad in new[] { "", "monitor.example.com", "ftp://example.com", "http://" })
        {
            var refused = Assert.Throws<ClientException>(() => new Client(bad, "k"));
            Assert.Equal(ClientProblem.BadAddress, refused.Problem);
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
            Assert.Equal(local, new Client(local, "k").Server.GetLeftPart(UriPartial.Authority));
        }
        foreach (var far in new[] { "http://monitor.example.com", "http://8.8.8.8", "http://172.32.0.1", "http://[2001:db8::1]" })
        {
            var refused = Assert.Throws<ClientException>(() => new Client(far, "k"));
            Assert.Equal(ClientProblem.NeedsHttps, refused.Problem);
            // Over https it's fine.
            Assert.False(new Client(far.Replace("http:", "https:"), "k").SendsKeyInTheClear);
        }
    }

    [Fact]
    public void ASilenceCoversTheAlertAndNothingElse()
    {
        var (alerts, _) = Decoded();
        var hour = TimeSpan.FromHours(1);
        // Its rule on its host.
        Assert.Equal(
            """{"duration_secs":3600,"filter":"id=22ad9264b6eb708ffdbedcd5cc073e5c","reason":"Silenced from the notification area","rule_id":1}""",
            Client.SilenceBody(alerts.Open[0], hour));
        // Only its own container, not the rule's others on the same host.
        Assert.Equal(
            """{"duration_secs":3600,"filter":"id=a","reason":"Silenced from the notification area","rule_id":2,"series":"web"}""",
            Client.SilenceBody(alerts.Resolved[0], hour));
        // One about a group of hosts has no host to name, and a filter would leave it out.
        var fleet = Read<ServerAlert>("""
            {"id": 11, "rule_id": 6, "rule_name": "Half the web hosts down", "severity": "critical",
             "host_id": "fleet", "hostname": "Fleet", "series": "", "summary": "Half down",
             "started_ms": 1, "resolved_ms": null, "acknowledged_by": null, "silence_id": null,
             "maintenance_window_id": null}
            """);
        Assert.Equal(
            """{"duration_secs":3600,"reason":"Silenced from the notification area","rule_id":6}""",
            Client.SilenceBody(fleet, hour));
        // Without its rule, which has been deleted, a silence would cover every rule.
        Assert.False(alerts.Open[1].CanBeSilenced);
        var refused = Assert.Throws<ClientException>(() => Client.SilenceBody(alerts.Open[1], hour));
        Assert.Equal(ClientProblem.CantSilence, refused.Problem);
    }

    [Fact]
    public async Task AnswersBecomeProblemsThatSayWhatWentWrong()
    {
        async Task<ClientProblem> Answered(int status, string body = "[]")
        {
            var client = new Client("https://monitor.example.com", "k", new Answer(status, body));
            var refused = await Assert.ThrowsAsync<ClientException>(() => client.GetHosts(TestContext.Current.CancellationToken));
            return refused.Problem;
        }
        Assert.Equal(ClientProblem.NotSignedIn, await Answered(401));
        Assert.Equal(ClientProblem.NotAllowed, await Answered(403));
        Assert.Equal(ClientProblem.Status, await Answered(500));
        Assert.Equal(ClientProblem.NotMaxNines, await Answered(200, "<html>"));
        var fine = new Client("https://monitor.example.com", "k", new Answer(200, HostsJson));
        Assert.Equal(8, (await fine.GetHosts(TestContext.Current.CancellationToken)).Count);
    }

    sealed class Answer(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status)
            {
                Content = new StringContent(body),
            });
    }

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
}
