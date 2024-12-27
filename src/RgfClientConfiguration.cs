using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Services;
using System.Reflection;

namespace Recrovit.RecroGridFramework.Client;

public class RgfClientConfiguration
{
    public static bool IsInitialized { get; internal set; } = false;

    public static string AppRootPath { get; internal set; } = "/";

    public static string Version => _version.Value;

    private static readonly Lazy<string> _version = new Lazy<string>(() => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);

    public static Dictionary<string, string> ClientVersions { get; } = [];

    public static Version MinimumRgfCoreVersion = new Version(8, 13, 0);//RGF.Core MinVersion
}

public static class RgfClientConfigurationExtension
{
    public static IServiceCollection AddRgfServices(this IServiceCollection services, IConfiguration configuration, ILogger? logger = null)
    {
        var config = configuration.GetSection("Recrovit:RecroGridFramework");
        ApiService.BaseAddress = config.GetValue<string>("API:BaseAddress", string.Empty)!;
        var root = config.GetValue("AppRootPath", config.GetValue("AppRootUrl", ""));
        if (!string.IsNullOrEmpty(root))
        {
            RgfClientConfiguration.AppRootPath = root.EndsWith('/') ? root : root + "/";
        }
        logger?.LogInformation("AddRgfServices: AppRootPath={AppRootPath} ApiService.BaseAddress={BaseAddress}", RgfClientConfiguration.AppRootPath, ApiService.BaseAddress);

        if (string.IsNullOrEmpty(ApiService.BaseAddress))
        {
            const string msg = "The 'Recrovit:RecroGridFramework:API:BaseAddress' configuration setting is missing or invalid.";
            logger?.LogCritical(msg);
            throw new InvalidOperationException(msg);
        }

        RgfClientConfiguration.ClientVersions.TryAdd(RgfHeaderKeys.RgfClientVersion, RgfClientConfiguration.Version);

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

    public static async Task InitializeRgfClientAsync(this IServiceProvider serviceProvider, bool clientSideRendering = true)
    {
        if (!RgfClientConfiguration.IsInitialized)
        {
            if (clientSideRendering)
            {
                var recroDict = serviceProvider.GetRequiredService<IRecroDictService>();
                await recroDict.InitializeAsync();
                _ = serviceProvider.GetRequiredService<IRecroSecService>();
            }
            var logger = serviceProvider.GetRequiredService<ILogger<RgfClientConfiguration>>();
            logger?.LogInformation("RecroGrid Framework Client v{Version} initialized.", RgfClientConfiguration.Version);
            RgfClientConfiguration.IsInitialized = true;
        }
    }
}