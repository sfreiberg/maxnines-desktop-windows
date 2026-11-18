using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaxNines.Kit;

/// <summary>How bad an alert is, mildest first, so the worse of two is the greater.</summary>
[JsonConverter(typeof(SeverityConverter))]
public enum Severity
{
    Warning,
    Critical,
}

/// <summary>A severity added to the server later is at least a warning.</summary>
sealed class SeverityConverter : JsonConverter<Severity>
{
    public override Severity Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.GetString() == "critical" ? Severity.Critical : Severity.Warning;

    public override void Write(Utf8JsonWriter writer, Severity value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == Severity.Critical ? "critical" : "warning");
}

/// <summary>
/// An alert as the server lists it. Fields this app doesn't use are left out, and the
/// server may add more.
/// </summary>
public sealed record ServerAlert
{
    public required long Id { get; init; }
    public long? RuleId { get; init; }
    public required string RuleName { get; init; }
    public required Severity Severity { get; init; }
    public required string HostId { get; init; }
    public required string Hostname { get; init; }

    /// <summary>
    /// Which of its rule's series it's about, such as a check's name. Empty for alerts
    /// about the host as a whole, and missing from older servers.
    /// </summary>
    public string Series { get; init; } = "";

    /// <summary>What was wrong, as it said when it fired.</summary>
    public required string Summary { get; init; }
    public required long StartedMs { get; init; }
    public long? ResolvedMs { get; init; }

    /// <summary>
    /// Where things stood as it cleared, such as "web-1 is back online". Missing for one
    /// closed for another reason, such as its rule being deleted, and from older servers.
    /// </summary>
    public string? ResolvedSummary { get; init; }

    public string? AcknowledgedBy { get; init; }
    public long? SilenceId { get; init; }
    public long? MaintenanceWindowId { get; init; }

    public bool IsAcknowledged => AcknowledgedBy is not null;

    /// <summary>
    /// A silence for one alert names its rule, so one whose rule has been deleted has
    /// nothing to name.
    /// </summary>
    public bool CanBeSilenced => RuleId is not null;

    /// <summary>Whether a silence or a maintenance window is keeping its notifications back.</summary>
    public bool IsSilenced => SilenceId is not null || MaintenanceWindowId is not null;

    /// <summary>
    /// What it says now: where things stood as it cleared once it has, and what's wrong
    /// until then, as the server's own notifications say.
    /// </summary>
    public string SaysNow => ResolvedSummary ?? Summary;
}

/// <summary>What <c>GET /api/v1/alerts</c> returns.</summary>
public sealed record Alerts
{
    public IReadOnlyList<ServerAlert> Open { get; init; } = [];
    public IReadOnlyList<ServerAlert> Resolved { get; init; } = [];
}

/// <summary>
/// A silence: while it runs, the alerts it covers fire and show, but nobody is told
/// about them.
/// </summary>
public sealed record Silence
{
    public required long Id { get; init; }

    /// <summary>The alert rule it covers, or every rule.</summary>
    public long? RuleId { get; init; }

    /// <summary>Which hosts it covers, such as <c>id=7f3c…</c> or <c>hostname=db*</c>. Empty for all.</summary>
    public string Filter { get; init; } = "";

    /// <summary>One series alone, such as a check's name. Empty for all.</summary>
    public string Series { get; init; } = "";

    public required string Reason { get; init; }
    public required long EndsMs { get; init; }
}

/// <summary>What <c>GET /api/v1/silences</c> returns. Ones that have ended aren't used.</summary>
public sealed record Silences
{
    public IReadOnlyList<Silence> Active { get; init; } = [];
}

/// <summary>A check the server runs itself, as <c>GET /api/v1/remote-checks</c> lists it.</summary>
public sealed record RemoteCheck
{
    /// <summary>How it last went.</summary>
    public sealed record LatestRun
    {
        public required bool Up { get; init; }
    }

    public required long Id { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>
    /// Missing until it's run since the server started, and from servers too old to say.
    /// </summary>
    public LatestRun? Latest { get; init; }
}

/// <summary>A host the server watches.</summary>
public sealed record MonitoredHost
{
    public required string Id { get; init; }
    public required string Hostname { get; init; }
    public string PublicKey { get; init; } = "";
    public required string State { get; init; }
    public required bool Online { get; init; }

    /// <summary>
    /// <c>agent</c>, <c>agentless</c> or <c>built_in</c>. A server from before hosts could
    /// go without an agent doesn't say.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>How it is, such as <c>online</c>, <c>offline</c> or <c>not_read</c>.</summary>
    public string? Health { get; init; }

    /// <summary>
    /// Whether it's a host that could be online: approved, not one of the server's own
    /// built-in hosts, and not one with no agent that nothing is read for, which is
    /// neither online nor offline.
    /// </summary>
    public bool IsCounted
    {
        get
        {
            if (State != "approved" || Health == "not_read")
            {
                return false;
            }
            // Without a kind, only a built-in host has no key.
            return Kind is null ? PublicKey.Length > 0 : Kind != "built_in";
        }
    }
}
