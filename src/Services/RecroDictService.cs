using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RecroDictService : IRecroDictService
{
    private readonly ILogger _logger;
    private readonly IRgfApiService _apiService;

    public RecroDictService(IConfiguration configuration, ILogger<RecroDictService> logger, IRgfApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;
        _dictionaryCache = new ConcurrentDictionary<string, MemoryCache>();

        var config = configuration.GetSection("Recrovit:RecroGridFramework:RecroDict");
        DefaultLanguage = config.GetValue<string>("DefaultLanguage", "eng")!;
        Languages = new Dictionary<string, string>() { { DefaultLanguage, DefaultLanguage } };
        _rgfUi = new Dictionary<string, string>();
    }

    public async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            var dict = await GetDictionaryAsync("RGF.Language", DefaultLanguage, false);
            Languages = dict.ToDictionary(k => k.Key, v => v.Value);

            dict = await GetDictionaryAsync("RGF.UI", DefaultLanguage, false);
            _rgfUi = dict.ToDictionary(k => k.Key, v => v.Value);

            _logger.LogDebug("Initialized");
            _isInitialized = true;
        }
    }


    public string DefaultLanguage { get; private set; }

    public Dictionary<string, string> Languages { get; private set; }

    private bool _isInitialized = false;

    private ConcurrentDictionary<string, MemoryCache> _dictionaryCache { get; }

    private Dictionary<string, string> _rgfUi { get; set; }

    public async Task SetDefaultLanguageAsync(string language)
    {
        if (language != null && DefaultLanguage != language)
        {
            _logger.LogDebug("Change language: {language}", language);
            _isInitialized = false;
            DefaultLanguage = language;
            await InitializeAsync();
        }
    }

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
        if (!_isInitialized)
        {
            return "RecroDict has not been initialized";
        }
        return IRecroDictServiceExtension.GetItem(_rgfUi, stringId, $"RGF.UI.{stringId}");
    }
}

