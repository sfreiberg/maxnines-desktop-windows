namespace MaxNines.Kit;

/// <summary>What the app shows, worked out from what the server lists.</summary>
public sealed class Overview
{
    /// <summary>Firing now, worst first and then newest first.</summary>
    public IReadOnlyList<ServerAlert> Firing { get; }

    /// <summary>What ended in the last hour, newest first.</summary>
    public IReadOnlyList<ServerAlert> Resolved { get; }

    public int HostsOnline { get; }
    public int Hosts { get; }

    /// <summary>The server's own checks, unless it has none or couldn't say.</summary>
    public CheckCount? Checks { get; }

    /// <summary>Silences running now, ending soonest first.</summary>
    public IReadOnlyList<SilenceRow> Silences { get; }

    const long Recently = 3_600_000;

    /// <summary>More than this many resolved alerts is a list nobody reads.</summary>
    const int MostResolved = 5;

    public Overview(
        Alerts alerts, IReadOnlyList<MonitoredHost> hosts, IReadOnlyList<RemoteCheck>? checks = null,
        IReadOnlyList<Silence>? silences = null, DateTimeOffset? now = null)
    {
        var nowMs = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        Firing = alerts.Open
            .OrderByDescending(alert => alert.Severity)
            .ThenByDescending(alert => alert.StartedMs)
            .ToList();
        Resolved = alerts.Resolved
            .Where(alert => (alert.ResolvedMs ?? 0) >= nowMs - Recently)
            .OrderByDescending(alert => alert.ResolvedMs ?? 0)
            .Take(MostResolved)
            .ToList();
        var counted = hosts.Where(host => host.IsCounted).ToList();
        Hosts = counted.Count;
        HostsOnline = counted.Count(host => host.Online);
        Checks = checks is null ? null : CheckCount.Of(checks);

        // Alerts are the only place this app learns a rule's name.
        var rules = new Dictionary<long, string>();
        foreach (var alert in alerts.Open.Concat(alerts.Resolved))
        {
            if (alert.RuleId is { } rule)
            {
                rules[rule] = alert.RuleName;
            }
        }
        Silences = (silences ?? [])
            .OrderBy(silence => silence.EndsMs)
            .Select(silence => new SilenceRow(silence, rules, hosts))
            .ToList();
    }

    public int Critical => Firing.Count(alert => alert.Severity == Severity.Critical);
    public int Warnings => Firing.Count(alert => alert.Severity == Severity.Warning);

    /// <summary>The worst thing firing, which is what the icon says.</summary>
    public Severity? Worst => Firing.Count == 0 ? null : Firing.Max(alert => alert.Severity);

    /// <summary>The whole state in a few words, such as <c>2 critical, 1 warning</c>.</summary>
    public string Headline
    {
        get
        {
            var parts = new List<string>();
            if (Critical > 0)
            {
                parts.Add($"{Critical} critical");
            }
            if (Warnings > 0)
            {
                parts.Add(Warnings == 1 ? "1 warning" : $"{Warnings} warnings");
            }
            return parts.Count == 0 ? "Nothing firing" : string.Join(", ", parts);
        }
    }
}

/// <summary>How the server's own checks are doing. Paused ones aren't counted.</summary>
public sealed record CheckCount(int Passing, int Run, int Enabled)
{
    /// <summary>Nothing, when there are no checks to count.</summary>
    internal static CheckCount? Of(IReadOnlyList<RemoteCheck> checks)
    {
        var enabled = checks.Where(check => check.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return null;
        }
        // Only checks that have run since the server started can pass or fail.
        return new CheckCount(
            enabled.Count(check => check.Latest?.Up == true),
            enabled.Count(check => check.Latest is not null),
            enabled.Count);
    }

    public int Failing => Run - Passing;
}

/// <summary>A silence as the app lists it.</summary>
public sealed record SilenceRow
{
    public long Id { get; }
    public string Reason { get; }

    /// <summary>What it keeps quiet, such as <c>High CPU on web-1</c>.</summary>
    public string Covers { get; }

    public long EndsMs { get; }

    internal SilenceRow(Silence silence, Dictionary<long, string> rules, IReadOnlyList<MonitoredHost> hosts)
    {
        Id = silence.Id;
        Reason = silence.Reason;
        EndsMs = silence.EndsMs;
        var what = new List<string>();
        if (silence.RuleId is { } rule)
        {
            what.Add(rules.GetValueOrDefault(rule, "One alert rule"));
        }
        if (silence.Series.Length > 0)
        {
            what.Add(silence.Series);
        }
        var filter = silence.Filter.Trim();
        var host = filter.StartsWith("id=", StringComparison.Ordinal)
            ? hosts.FirstOrDefault(host => $"id={host.Id}" == filter)
            : null;
        var where = filter.Length == 0 ? "every host" : host?.Hostname ?? filter;
        Covers = what.Count == 0
            ? (filter.Length == 0 ? "Every alert" : $"Everything on {where}")
            : $"{string.Join(" ", what)} on {where}";
    }
}

public static class Times
{
    /// <summary>How long ago something was, in the largest whole unit, such as <c>4m</c> or <c>2h</c>.</summary>
    public static string Age(long thenMs, DateTimeOffset? now = null) =>
        Span(((now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() - thenMs) / 1000);

    /// <summary>How long until something happens, in the same units as <see cref="Age"/>.</summary>
    public static string Left(long thenMs, DateTimeOffset? now = null) =>
        Span((thenMs - (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() + 999) / 1000);

    static string Span(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return seconds switch
        {
            < 60 => $"{seconds}s",
            < 3600 => $"{seconds / 60}m",
            < 86_400 => $"{seconds / 3600}h",
            _ => $"{seconds / 86_400}d",
        };
    }
}
