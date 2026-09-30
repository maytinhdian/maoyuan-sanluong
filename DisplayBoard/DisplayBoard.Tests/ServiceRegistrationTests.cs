using DisplayBoard.Core;
using DisplayBoard.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayBoard.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddDisplayBoardCore_ResolvesConfigurationService()
    {
        var path = Path.Combine(Path.GetTempPath(), "display-config.json");
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDisplayBoardCore(path)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var service = provider.GetRequiredService<IConfigurationService>();

        Assert.Equal(path, service.FilePath);
        Assert.Same(service, provider.GetRequiredService<IConfigurationService>());
    }
}
