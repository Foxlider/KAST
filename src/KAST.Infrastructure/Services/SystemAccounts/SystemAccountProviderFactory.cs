using System.Runtime.InteropServices;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services.SystemAccounts;

public static class SystemAccountProviderFactory
{
    public static ISystemAccountProvider Create()
        => Create(DetectHostKind());

    public static ISystemAccountProvider Create(SystemAccountHostKind hostKind)
        => hostKind switch
        {
            SystemAccountHostKind.Windows => new WindowsSystemAccountProvider(),
            SystemAccountHostKind.Linux or SystemAccountHostKind.LinuxContainer => new LinuxSystemAccountProvider(),
            _ => new UnsupportedSystemAccountProvider("System account authentication is only supported on Windows and Linux.")
        };

    public static SystemAccountHostKind DetectHostKind()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SystemAccountHostKind.Windows;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return IsRunningInContainer()
                ? SystemAccountHostKind.LinuxContainer
                : SystemAccountHostKind.Linux;

        return SystemAccountHostKind.Unsupported;
    }

    private static bool IsRunningInContainer()
        => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
           File.Exists("/.dockerenv");
}

public enum SystemAccountHostKind
{
    Windows,
    Linux,
    LinuxContainer,
    Unsupported
}
