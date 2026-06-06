using KAST.Infrastructure.Services.SystemAccounts;

namespace KAST.Tests;

public class SystemAccountProviderFactoryTests
{
    [Theory]
    [InlineData(SystemAccountHostKind.Windows, "Windows")]
    [InlineData(SystemAccountHostKind.Linux, "Linux Local")]
    [InlineData(SystemAccountHostKind.LinuxContainer, "Linux Local")]
    [InlineData(SystemAccountHostKind.Unsupported, "Unsupported")]
    public void Create_ReturnsProviderForHostKind(SystemAccountHostKind hostKind, string expectedProvider)
    {
        var provider = SystemAccountProviderFactory.Create(hostKind);

        Assert.Equal(expectedProvider, provider.GetStatus().Provider);
    }
}
