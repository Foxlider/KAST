using KAST.Core.Enums;
using KAST.Core.Models;
using MudBlazor;

namespace KAST.UI.Helpers;

public static class UiHelpers
{
    public static Color GetStatusColor(ServerInstanceStatus status) => status switch
    {
        ServerInstanceStatus.Running => Color.Success,
        ServerInstanceStatus.Crashed => Color.Error,
        ServerInstanceStatus.Starting or ServerInstanceStatus.Downloading => Color.Warning,
        _ => Color.Default
    };

    public static string GetStatusIcon(ServerInstanceStatus status) => status switch
    {
        ServerInstanceStatus.Running => Icons.Material.Filled.PlayCircle,
        ServerInstanceStatus.Crashed => Icons.Material.Filled.Error,
        ServerInstanceStatus.Starting => Icons.Material.Filled.HourglassTop,
        ServerInstanceStatus.Downloading => Icons.Material.Filled.Downloading,
        _ => Icons.Material.Outlined.StopCircle
    };

    public static Color GetModStatusColor(ModStatus status) => status switch
    {
        ModStatus.Installed => Color.Success,
        ModStatus.Downloading or ModStatus.Updating => Color.Warning,
        ModStatus.Error => Color.Error,
        ModStatus.UpdateAvailable => Color.Warning,
        _ => Color.Default
    };

    public static string GetModStatusIcon(ModStatus status) => status switch
    {
        ModStatus.Installed => Icons.Material.Filled.CheckCircle,
        ModStatus.Downloading => Icons.Material.Filled.CloudDownload,
        ModStatus.Updating => Icons.Material.Filled.Sync,
        ModStatus.Error => Icons.Material.Filled.Error,
        ModStatus.UpdateAvailable => Icons.Material.Filled.NewReleases,
        _ => Icons.Material.Outlined.RadioButtonUnchecked
    };

    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0; double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:F1} {sizes[order]}";
    }

    public static string FormatRelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 60) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)}mo ago";
        return $"{(int)(elapsed.TotalDays / 365)}y ago";
    }
}
