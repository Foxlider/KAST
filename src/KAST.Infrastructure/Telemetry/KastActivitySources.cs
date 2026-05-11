using System.Diagnostics;

namespace KAST.Infrastructure.Telemetry;

/// <summary>
/// Central registry of all named <see cref="ActivitySource"/> instances used
/// for OpenTelemetry tracing across the KAST infrastructure layer.
/// The UI project adds each source name via <c>AddSource()</c> at startup.
/// </summary>
public static class KastActivitySources
{
    public const string ServiceName = "KAST";

    /// <summary>Content installs: mod downloads, server downloads, local installs.</summary>
    public static readonly ActivitySource Content = new("KAST.Content", "1.0.0");

    /// <summary>Server process lifecycle: start and stop.</summary>
    public static readonly ActivitySource Process = new("KAST.Process", "1.0.0");

    /// <summary>Mod operations: update checks, downloads, updates.</summary>
    public static readonly ActivitySource Mods = new("KAST.Mods", "1.0.0");
}
