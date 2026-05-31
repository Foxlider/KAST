namespace KAST.Core.Interfaces;

/// <summary>
/// Sanitizes text that can leave internal boundaries such as UI, APIs, logs,
/// telemetry, SignalR messages, and install status state.
/// </summary>
public interface IOutputSanitizer
{
    string Sanitize(string? value);

    string ToDisplayPath(string? path);

    string SanitizeException(Exception exception);
}
