namespace MaxNines.Kit;

/// <summary>Which changes someone wants a notification about.</summary>
public sealed record NotifyWhen(bool Critical, bool Warnings, bool Resolved)
{
    public bool Any => Critical || Warnings || Resolved;
}

/// <summary>One notification to show.</summary>
/// <param name="Id">Unique to the change, so the same one is never shown twice.</param>
/// <param name="Acknowledges">
/// The alert it's about, which can be acknowledged from it, if it's firing and nobody
/// has yet.
/// </param>
/// <param name="Path">The page it opens.</param>
public sealed record Notice(string Id, string Title, string Body, long? Acknowledges, string Path);

public static class Notices
{
    /// <summary>
    /// More than this many at once become one notification, so a storm doesn't bury
    /// the screen.
    /// </summary>
    const int Most = 3;

    /// <summary>
    /// What's worth a notification between what the server listed last time and now.
    /// The first time there's no last time, and nothing is: what was already firing
    /// when the app started isn't news. Silenced alerts stay quiet, as they do
    /// everywhere else in MaxNines.
    /// </summary>
    public static List<Notice> Between(Alerts old, Alerts current, NotifyWhen when, DateTimeOffset? now = null)
    {
        var before = old.Open.GroupBy(alert => alert.Id).ToDictionary(group => group.Key, group => group.First());
        var still = current.Open.Select(alert => alert.Id).ToHashSet();

        var started = new List<Notice>();
        var worst = Severity.Warning;
        foreach (var alert in current.Open.Where(alert => !alert.IsSilenced))
        {
            var was = before.GetValueOrDefault(alert.Id);
            // A new alert, one whose silence ended while it was still firing, as the
            // server tells people about then, or one that's got worse.
            var unsilenced = was?.IsSilenced == true;
            var worse = was?.Severity == Severity.Warning && alert.Severity == Severity.Critical;
            if (was is not null && !unsilenced && !worse)
            {
                continue;
            }
            if (!(alert.Severity == Severity.Critical ? when.Critical : when.Warnings))
            {
                continue;
            }
            var level = alert.Severity == Severity.Critical ? "Critical" : "Warning";
            if (alert.Severity == Severity.Critical)
            {
                worst = Severity.Critical;
            }
            started.Add(new Notice(
                $"alert-{alert.Id}-{(alert.Severity == Severity.Critical ? "critical" : "warning")}",
                $"{Mark(alert.Severity)} {(was is null || unsilenced ? level : "Now critical")}: {alert.RuleName}",
                alert.Summary,
                alert.IsAcknowledged ? null : alert.Id,
                Page(alert)));
        }

        var ended = new List<Notice>();
        if (when.Resolved)
        {
            var resolved = current.Resolved.GroupBy(alert => alert.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var alert in old.Open.Where(alert => !still.Contains(alert.Id) && !alert.IsSilenced))
            {
                // Only what the server says has resolved: one that's just gone, as when
                // its host was deleted, isn't news worth telling.
                if (!resolved.TryGetValue(alert.Id, out var done))
                {
                    continue;
                }
                var endedAt = done.ResolvedAt ?? now ?? DateTimeOffset.UtcNow;
                // Where things stand now, so it doesn't read as the problem again. One
                // closed for another reason has only what was wrong, said as past.
                var outcome = done.ResolvedSummary ?? $"Was: {done.Summary}";
                ended.Add(new Notice(
                    $"alert-{alert.Id}-resolved",
                    $"{ResolvedMark} Resolved: {alert.RuleName}",
                    $"{outcome}\nLasted {Times.Age(done.StartedAt, endedAt)}",
                    null,
                    Page(alert)));
            }
        }

        return
        [
            .. Gathered(started, $"{Mark(worst)} {started.Count} alerts started"),
            .. Gathered(ended, $"{ResolvedMark} {ended.Count} alerts resolved"),
        ];
    }

    /// <summary>
    /// The coloured circles the server's Discord messages use, so how bad it is, or
    /// that it's over, shows before a word is read.
    /// </summary>
    static string Mark(Severity severity) => severity == Severity.Critical ? "🔴" : "🟡";

    const string ResolvedMark = "🟢";

    /// <summary>Many at once become one, which opens the alerts page.</summary>
    static List<Notice> Gathered(List<Notice> notices, string title)
    {
        if (notices.Count <= Most)
        {
            return notices;
        }
        var first = notices.Take(Most).Select(notice => notice.Body.Split('\n')[0]);
        return
        [
            new Notice(
                string.Join(",", notices.Select(notice => notice.Id)),
                title,
                string.Join("\n", first.Append($"and {notices.Count - Most} more")),
                null,
                "/alerts"),
        ];
    }

    /// <summary>The page an alert opens: its host's.</summary>
    public static string Page(ServerAlert alert) => $"/hosts/{alert.HostId}";
}
