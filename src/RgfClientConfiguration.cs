using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Services;

namespace Recrovit.RecroGridFramework.Client;

public static class RgfClientConfiguration
{
    public static IServiceCollection AddRgfServices(this IServiceCollection services, IConfiguration configuration, ILogger? logger = null)
    {
        logger?.LogInformation("Initializing configuration for RecroGrid Framework Client.");

        var config = configuration.GetSection("Recrovit:RecroGridFramework");
        ApiService.BaseAddress = config.GetValue<string>("API:BaseAddress", string.Empty)!;
        var root = config.GetValue<string>("AppRootUrl");
        if (!string.IsNullOrEmpty(root))
        {
            AppRootUrl = root.EndsWith('/') ? root : root + "/";
        }

        if (string.IsNullOrEmpty(ApiService.BaseAddress))
        {
            string msg = "The 'Recrovit:RecroGridFramework:API:BaseAddress' configuration setting is missing or invalid.";
            logger?.LogCritical(msg);
            throw new InvalidOperationException(msg);
        }

        services.AddHttpClient(ApiService.RgfApiClientName, httpClient =>
        {
            httpClient.BaseAddress = new Uri(ApiService.BaseAddress);
        });

        services.AddSingleton<IRgfEventNotificationService, RgfEventNotificationService>();
        services.AddSingleton<IRgfApiService, ApiService>();
        services.AddScoped<IRecroDictService, RecroDictService>();
        services.AddScoped<IRecroSecService, RecroSecService>();
        services.AddScoped<IRgfMenuService, MenuService>();

        return services;
    }

    public static async Task InitializeRgfClientAsync(this IServiceProvider serviceProvider)
    {
        if (!_initialized)
        {
            var recroDict = serviceProvider.GetRequiredService<IRecroDictService>();
            await recroDict.InitializeAsync();
            _initialized = true;
        }
    }

    private static bool _initialized = false;
    public static string AppRootUrl { get; private set; } = "/";
}
