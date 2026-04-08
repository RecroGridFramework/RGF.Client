using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Handlers;
using Recrovit.RecroGridFramework.Client.Services;
using System.Reflection;

namespace Recrovit.RecroGridFramework.Client;

public class RgfClientConfiguration
{
    public static bool IsInitialized { get; internal set; } = false;

    public static string AppRootPath { get; internal set; } = string.Empty;

    public static string ExternalApiBaseAddress { get; internal set; } = string.Empty;

    public static string ProxyApiBaseAddress { get; internal set; } = string.Empty;

    public static RgfApiAuthMode ApiAuthMode { get; internal set; } = RgfApiAuthMode.None;

    public static string Version => _version.Value;

    private static readonly Lazy<string> _version = new Lazy<string>(() => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);

    public static Dictionary<string, string> ClientVersions { get; } = [];

    public static Version MinimumRgfCoreVersion = new Version(10, 0, 0);//RGF.Core MinVersion
}

public static class RgfClientConfigurationExtension
{
    public static IServiceCollection AddRgfServices(this IServiceCollection services, IConfiguration configuration, ILogger? logger = null,
        RgfApiAuthMode authMode = RgfApiAuthMode.None)
    {
        var config = configuration.GetSection("Recrovit:RecroGridFramework");
        services.AddOptions<RgfAuthenticationOptions>()
            .Bind(config.GetSection("Authentication"));
        var externalBaseAddress = config.GetValue<string>("API:BaseAddress", string.Empty)!.TrimEnd('/');
        var configuredProxyBaseAddress = config.GetValue<string>("API:ProxyBaseAddress", string.Empty);
        var effectiveProxyBaseAddress = ResolveProxyBaseAddress(authMode, externalBaseAddress, configuredProxyBaseAddress);
        var root = config.GetValue("AppRootPath", config.GetValue("AppRootUrl", ""));
        if (!string.IsNullOrEmpty(root))
        {
            RgfClientConfiguration.AppRootPath = root.TrimEnd('/');
        }
        ApiService.ExternalBaseAddress = externalBaseAddress;
        ApiService.BaseAddress = effectiveProxyBaseAddress;
        RgfClientConfiguration.ExternalApiBaseAddress = externalBaseAddress;
        RgfClientConfiguration.ProxyApiBaseAddress = effectiveProxyBaseAddress;
        RgfClientConfiguration.ApiAuthMode = authMode;

        logger?.LogInformation("AddRgfServices: AppRootPath={AppRootPath}, ProxyApiBaseAddress={ProxyApiBaseAddress}, ExternalApiBaseAddress={ExternalApiBaseAddress}, ApiAuthMode={ApiAuthMode}",
            RgfClientConfiguration.AppRootPath, RgfClientConfiguration.ProxyApiBaseAddress, RgfClientConfiguration.ExternalApiBaseAddress, RgfClientConfiguration.ApiAuthMode);

        if (string.IsNullOrEmpty(ApiService.BaseAddress))
        {
            var msg = authMode is RgfApiAuthMode.ServerProxy or RgfApiAuthMode.ServerProxySsr
                ? "The proxy base address is missing or invalid. Set 'Recrovit:RecroGridFramework:API:ProxyBaseAddress'."
                : "The 'Recrovit:RecroGridFramework:API:BaseAddress' configuration setting is missing or invalid.";
            logger?.LogCritical(msg);
            throw new InvalidOperationException(msg);
        }

        RgfClientConfiguration.ClientVersions.TryAdd(RgfHeaderKeys.RgfClientVersion, RgfClientConfiguration.Version);

        services.AddHttpClient(ApiService.RgfApiClientName, httpClient =>
        {
            httpClient.BaseAddress = new Uri(ApiService.BaseAddress);
        });
        services.AddHttpClient(ApiService.RgfAuthApiClientName, httpClient =>
        {
            httpClient.BaseAddress = new Uri(ApiService.BaseAddress);
        });

        services.AddSingleton<IRgfApiService, ApiService>();
        services.AddSingleton<RgfAuthenticationPrincipalFactory>();
        services.AddScoped<IRgfAccessTokenAccessor, NoOpRgfAccessTokenAccessor>();
        services.AddSingleton<IRgfAuthenticationFailureHandler, NoOpRgfAuthenticationFailureHandler>();
        services.AddScoped<IRgfEventNotificationService, RgfEventNotificationService>();
        services.AddScoped<IRecroSecService, RecroSecService>();
        services.AddScoped<IRecroDictService, RecroDictService>();
        services.AddScoped<IRgfMenuService, MenuService>();
        services.AddTransient<IRgfProgressService, RgfProgressService>();

        return services;
    }

    private static string ResolveProxyBaseAddress(RgfApiAuthMode authMode, string externalBaseAddress, string configuredProxyBaseAddress)
    {
        if (!string.IsNullOrWhiteSpace(configuredProxyBaseAddress))
        {
            return configuredProxyBaseAddress.TrimEnd('/');
        }

        return authMode is RgfApiAuthMode.ServerProxy or RgfApiAuthMode.ServerProxySsr
            ? string.Empty
            : externalBaseAddress;
    }

    public static async Task InitializeRgfClientAsync(this IServiceProvider serviceProvider, bool clientSideRendering = true)
    {
        if (!RgfClientConfiguration.IsInitialized)
        {
            RgfLoggerFactory.Initialize(serviceProvider.GetRequiredService<ILoggerFactory>());

            if (clientSideRendering)
            {
                var recroDict = serviceProvider.GetRequiredService<IRecroDictService>();
                string? language = null;
                if (RgfClientConfiguration.ApiAuthMode == RgfApiAuthMode.WasmBearer)
                {
                    language = serviceProvider.GetService<IRecroSecService>()?.UserLanguage;
                }
                await recroDict.InitializeAsync(language);
            }
            var logger = serviceProvider.GetRequiredService<ILogger<RgfClientConfiguration>>();
            logger?.LogInformation("RecroGrid Framework Client v{Version} initialized.", RgfClientConfiguration.Version);
            RgfClientConfiguration.IsInitialized = true;
        }
    }
}
