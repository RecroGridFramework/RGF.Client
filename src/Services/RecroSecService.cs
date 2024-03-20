using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Events;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Security;
using System.Data;
using System.Security.Claims;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RecroSecServiceOptions
{
    public string? RoleClaimType { get; set; }
}

internal class RecroSecService : IRecroSecService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IRgfApiService _apiService;
    private readonly IServiceProvider _serviceProvider;
    private readonly RecroSecServiceOptions _options = new();
    private readonly AuthenticationStateProvider? _authenticationStateProvider;

    public RecroSecService(IConfiguration configuration, ILogger<RecroSecService> logger, IRgfApiService apiService, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _apiService = apiService;
        _serviceProvider = serviceProvider;
        configuration.Bind("Recrovit:RecroGridFramework:RecroSec", _options);
        _authenticationStateProvider = serviceProvider.GetService<AuthenticationStateProvider>();
        if (_authenticationStateProvider != null)
        {
            _authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
            OnAuthenticationStateChanged(_authenticationStateProvider.GetAuthenticationStateAsync());
        }
    }

    public void Dispose()
    {
        if (_authenticationStateProvider != null)
        {
            _authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }
    }

    public bool IsAuthenticated => CurrentUser.Identity?.IsAuthenticated == true;

    public string? UserName => CurrentUser.Identity?.Name;

    public string UserLanguage
    {
        get
        {
            if (string.IsNullOrEmpty(_userLanguage))
            {
                if (IsAuthenticated)
                {
                    var languageClaim = CurrentUser.FindFirst("Language");
                    _userLanguage = languageClaim?.Value;
                }
                if (string.IsNullOrEmpty(_userLanguage))
                {
                    var recroDict = _serviceProvider.GetRequiredService<IRecroDictService>();
                    _userLanguage = recroDict.DefaultLanguage;
                }
            }
            return _userLanguage;
        }
    }

    public async Task<string?> SetUserLanguageAsync(string language)
    {
        string? prev = _userLanguage;
        if (language != null && !language.Equals(UserLanguage, StringComparison.OrdinalIgnoreCase))
        {
            language = language.ToLower();
            var recroDict = _serviceProvider.GetRequiredService<IRecroDictService>();
            if (recroDict.Languages.ContainsKey(language))
            {
                var res = await SetLangAsync(language);
                if (res)
                {
                    _ = await _apiService.GetUserStateAsync(new() { { "language", language } });//save language setting
                }
            }
        }
        return prev;
    }

    public bool IsAdmin { get; private set; }

    public ClaimsPrincipal CurrentUser { get; private set; } = new();

    public async Task<string?> GetAccessTokenAsync()
    {
        var tokenProvider = _serviceProvider.GetRequiredService<IAccessTokenProvider>();
        var tokenResult = await tokenProvider.RequestAccessToken();
        if (tokenResult.TryGetToken(out var token))
        {
            return token.Value;
        }
        return null;
    }

    public List<string> RoleClaim
    {
        get
        {
            List<string> userRoles = new();
            if (IsAuthenticated)
            {
                var identities = CurrentUser.Identities.ToArray();
                for (int i = 0; i < identities.Length; i++)
                {
                    var roleClaim = _options.RoleClaimType ?? identities[i].RoleClaimType;
                    var roles = identities[i].Claims.Where(e => e.Type == roleClaim).Select(e => e.Value).ToArray();
                    if (roles.Length == 1 && roles[0].StartsWith('[') && roles[0].EndsWith(']'))
                    {
                        roles = roles[0].Replace("[", "").Replace("]", "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim(' ', '"'))
                            .ToArray();
                    }
                    userRoles.AddRange(roles);
                }
            }
            return userRoles;
        }
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
    {
        IsAdmin = false;
        var authenticationState = await stateTask;
        CurrentUser = authenticationState.User ?? new();
        //var roles = CurrentUser.FindFirst("role")?.Value ?? CurrentUser.FindFirst("roles")?.Value : "?";
        _logger.LogInformation("IsAuthenticated:{IsAuthenticated}, UserName:{UserName}, RoleClaims:{Roles}", IsAuthenticated, UserName, string.Join(", ", RoleClaim));
        if (IsAuthenticated)
        {
            var resp = await _apiService.GetUserStateAsync();
            if (resp.Success && resp.Result.IsValid)
            {
                IsAdmin = resp.Result.IsAdmin;
                if (!string.IsNullOrEmpty(resp.Result.Language))
                {
                    await SetLangAsync(resp.Result.Language);
                }
            }
        }
    }

    private async Task<bool> SetLangAsync(string language)
    {
        if (language != null && !language.Equals(_userLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _userLanguage = language;
            _logger.LogInformation("SetLang:{language}", language);
            var recroDict = _serviceProvider.GetRequiredService<IRecroDictService>();
            await recroDict.InitializeAsync(language);
            _ = LanguageChangedEvent.InvokeAsync(new(language));
            return true;
        }
        return false;
    }

    public async Task<RgfPermissions> GetEntityPermissionsAsync(string entityName, string? objectKey = null, int expiration = 60)
    {
        var res = await GetPermissionsAsync([new RecroSecQuery() { EntityName = entityName, ObjectKey = objectKey }], expiration);
        return res.Single().Permissions;
    }

    public async Task<RgfPermissions> GetPermissionsAsync(string objectName, string? objectKey = null, int expiration = 60)
    {
        var res = await GetPermissionsAsync([new RecroSecQuery() { ObjectName = objectName, ObjectKey = objectKey }], expiration);
        return res.Single().Permissions;
    }

    public async Task<List<RecroSecResult>> GetPermissionsAsync(IEnumerable<RecroSecQuery> query, int expiration = 60)
    {
        var res = new List<RecroSecResult>();
        var req = new List<RecroSecQuery>();
        foreach (var queryItem in query)
        {
            var key = $"{queryItem.EntityName}/{queryItem.ObjectName}/{queryItem.ObjectKey}";
            if (_recrosSecCache.TryGetValue(key, out RgfPermissions? perm) && perm != null)
            {
                res.Add(new(queryItem, perm));
            }
            else
            {
                req.Add(queryItem);
            }
        }
        if (req.Count != 0)
        {
            var resp = await _apiService.GetPermissionsAsync(req);
            if (resp.Success)
            {
                var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(expiration));
                foreach (var item in resp.Result)
                {
                    var key = $"{item.Query.EntityName}/{item.Query.ObjectName}/{item.Query.ObjectKey}";
                    _recrosSecCache.Set(key, item.Permissions, options);
                    res.Add(item);
                }
            }
        }
        return res;
    }

    public EventDispatcher<DataEventArgs<string>> LanguageChangedEvent { get; } = new();

    private string? _userLanguage;

    private MemoryCache _recrosSecCache { get; } = new MemoryCache(new MemoryCacheOptions());
}