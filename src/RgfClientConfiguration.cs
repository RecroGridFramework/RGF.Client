using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Services;
using System.Reflection;

namespace Recrovit.RecroGridFramework.Client;

public class RgfClientConfiguration
{
    public static bool IsInitialized { get; internal set; } = false;
    public static string AppRootUrl { get; internal set; } = "/";
}

public static class RgfClientConfigurationExtension
{
    public static IServiceCollection AddRgfServices(this IServiceCollection services, IConfiguration configuration, ILogger? logger = null)
    {
        var config = configuration.GetSection("Recrovit:RecroGridFramework");
        ApiService.BaseAddress = config.GetValue<string>("API:BaseAddress", string.Empty)!;
        var root = config.GetValue<string>("AppRootUrl");
        if (!string.IsNullOrEmpty(root))
        {
            RgfClientConfiguration.AppRootUrl = root.EndsWith('/') ? root : root + "/";
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

        services.AddSingleton<IRgfApiService, ApiService>();
        services.AddScoped<IRgfEventNotificationService, RgfEventNotificationService>();
        services.AddScoped<IRecroSecService, RecroSecService>();
        services.AddScoped<IRecroDictService, RecroDictService>();
        services.AddScoped<IRgfMenuService, MenuService>();

        return services;
    }

    public static async Task InitializeRgfClientAsync(this IServiceProvider serviceProvider)
    {
        if (!RgfClientConfiguration.IsInitialized)
        {
            var recroDict = serviceProvider.GetRequiredService<IRecroDictService>();
            _ = serviceProvider.GetRequiredService<IRecroSecService>();
            await recroDict.InitializeAsync();
            var ver = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            var logger = serviceProvider.GetRequiredService<ILogger<RgfClientConfiguration>>();
            logger?.LogInformation($"RecroGrid Framework Client v{ver} initialized.");
            RgfClientConfiguration.IsInitialized = true;
        }
    }
}
