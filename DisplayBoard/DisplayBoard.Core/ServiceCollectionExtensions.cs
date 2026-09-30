using DisplayBoard.Core.Configuration;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayBoard.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Core services.</summary>
    /// <param name="configurationFilePath">Full path of <c>display-config.json</c>.</param>
    public static IServiceCollection AddDisplayBoardCore(this IServiceCollection services, string configurationFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFilePath);

        services.AddSingleton(new ConfigurationOptions { FilePath = configurationFilePath });
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        return services;
    }
}
