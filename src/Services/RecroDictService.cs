using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using System.Collections.Concurrent;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RecroDictService : IRecroDictService
{
    private readonly ILogger _logger;
    private readonly IRgfApiService _apiService;

    public RecroDictService(IConfiguration configuration, ILogger<RecroDictService> logger, IRgfApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;

        var config = configuration.GetSection("Recrovit:RecroGridFramework:RecroDict");
        DefaultLanguage = (config.GetValue<string>("DefaultLanguage", "eng") ?? "eng").ToLower();
        Languages = new Dictionary<string, string>() { { DefaultLanguage, DefaultLanguage } };
        _rgfUi = new Dictionary<string, string>();
    }

    public async Task InitializeAsync(string? language = null)
    {
        if (language == null)
        {
            language = DefaultLanguage;
        }
        if (!IsInitialized || language != _uiLanguage)
        {
            var dict = await GetDictionaryAsync("RGF.Language", language, false);
            Languages = dict.ToDictionary(k => k.Key, v => v.Value);

            dict = await GetDictionaryAsync("RGF.UI", language, false);
            _rgfUi = dict.ToDictionary(k => k.Key, v => v.Value);

            _logger.LogInformation($"RecroDict:{language} initialized.");
            _uiLanguage = language;
            IsInitialized = true;
        }
    }

    public string DefaultLanguage { get; private set; }

    public Dictionary<string, string> Languages { get; private set; }

    public bool IsInitialized { get; private set; }

    private static ConcurrentDictionary<string, MemoryCache> _dictionaryCache { get; } = new();

    private Dictionary<string, string> _rgfUi { get; set; }

    private string? _uiLanguage;

    public virtual async Task<ConcurrentDictionary<string, string>> GetDictionaryAsync(string scope, string? language = null, bool authClient = true)
    {
        if (string.IsNullOrEmpty(language))
        {
            language = DefaultLanguage;
        }
        var dictCache = _dictionaryCache.GetOrAdd(language, new MemoryCache(new MemoryCacheOptions()));
        var dict = await dictCache.GetOrCreateAsync(scope, async entry =>
        {
            var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(60));
            entry.SetOptions(options);
            var res = await _apiService.GetAsync<Dictionary<string, string>>($"/rgf/api/RecroDict/{scope}/{language}", authClient: authClient);
            if (res.Success)
            {
                return new ConcurrentDictionary<string, string>(res.Result, new CaseInsensitiveStringComparer());
            }
            return new ConcurrentDictionary<string, string>();
        });
        return dict!;
    }

    public string GetRgfUiString(string stringId)
    {
        if (!IsInitialized)
        {
            return "RecroDict has not been initialized";
        }
        return IRecroDictServiceExtension.GetItem(_rgfUi, stringId, $"RGF.UI.{stringId}");
    }
}

