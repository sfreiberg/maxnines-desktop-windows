using System.Reflection;

namespace MaxNines.Tray;

/// <summary>
/// Exactly which build this is, as the project file records it when it's compiled,
/// worded as the MaxNines web interface words its own.
/// </summary>
static class Build
{
    static readonly Assembly App = typeof(Build).Assembly;

    public static string Version =>
        App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>The git commit, ending in <c>-dirty</c> if it had uncommitted changes.</summary>
    public static string Commit => Metadata("Commit");

    /// <summary>When it was compiled, in UTC.</summary>
    public static string BuiltAt => Metadata("BuiltAt");

    public static string Describe() => $"MaxNines {Version} · {Commit} · built {BuiltAt}";

    static string Metadata(string key) =>
        App.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : "unknown";
}
