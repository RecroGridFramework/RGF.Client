using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Security;
using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RecroSecServiceOptions
{
    public string AdministratorRoleName { get; set; } = "Administrators";
}

internal class RecroSecService : IRecroSecService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IRgfApiService _apiService;
    private readonly IRecroDictService _recroDict;
    private readonly RecroSecServiceOptions _options = new();
    private AuthenticationStateProvider? _authenticationStateProvider;

    public RecroSecService(IConfiguration configuration, ILogger<RecroSecService> logger, IRgfApiService apiService, IRecroDictService recroDict, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _apiService = apiService;
        this._recroDict = recroDict;
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

    private MemoryCache _recrosSecCache { get; } = new MemoryCache(new MemoryCacheOptions());

    public bool IsAuthenticated => CurrentUser.Identity?.IsAuthenticated == true;

    public string? UserName => CurrentUser.Identity?.Name;

    public bool IsAdmin
    {
        get
        {
            bool isAdmin = false;
            if (IsAuthenticated)
            {
                isAdmin = CurrentUser.IsInRole(_options.AdministratorRoleName) == true;
                if (!isAdmin)
                {
                    foreach (var role in UserRoles)
                    {
                        isAdmin = role.Contains(_options.AdministratorRoleName);
                        if (isAdmin)
                        {
                            break;
                        }
                    }
                }
            }
            return isAdmin;
        }
    }
    public ClaimsPrincipal CurrentUser { get; private set; } = new();

    public List<string> UserRoles
    {
        get
        {
            List<string> userRoles = new();
            if (IsAuthenticated)
            {
                var identities = CurrentUser.Identities.ToArray();
                for (int i = 0; i < identities.Count(); i++)
                {
                    var roleClaim = identities[i].RoleClaimType;
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
        var authenticationState = await stateTask;
        CurrentUser = authenticationState.User ?? new();
        //var roles = CurrentUser.FindFirst("role")?.Value ?? CurrentUser.FindFirst("roles")?.Value : "?";
        _logger.LogDebug("IsAuthenticated:{IsAuthenticated}, UserName:{UserName}, Roles:{Roles}", IsAuthenticated, UserName, string.Join(", ", UserRoles));
        if (IsAuthenticated)
        {
            var resp = await _apiService.GetUserState();
            if (resp.Success && resp.Result.IsValid)
            {
                await _recroDict.SetDefaultLanguageAsync(resp.Result.Language);
            }
        }
    }

    public async Task<RgfPermissions> GetPermissionsAsync(string objectName, string objectKey, int expiration = 60)
    {
        var res = await GetPermissionsAsync(new RecroSecQuery[] { new RecroSecQuery() { ObjectName = objectName, ObjectKey = objectKey } }, expiration);
        return res.Single().Permissions;
    }

    public async Task<List<RecroSecResult>> GetPermissionsAsync(IEnumerable<RecroSecQuery> query, int expiration = 60)
    {
        var res = new List<RecroSecResult>();
        var req = new List<RecroSecQuery>();
        foreach (var queryItem in query)
        {
            var key = $"{queryItem.ObjectName}/{queryItem.ObjectKey}";
            if (_recrosSecCache.TryGetValue(key, out RgfPermissions? perm) && perm != null)
            {
                res.Add(new(queryItem, perm));
            }
            else
            {
                req.Add(queryItem);
            }
        }
        if (req.Any())
        {
            var resp = await _apiService.GetPermissions(req);
            if (resp.Success)
            {
                var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(expiration));
                foreach (var item in resp.Result)
                {
                    var key = $"{item.Query.ObjectName}/{item.Query.ObjectKey}";
                    _recrosSecCache.Set(key, item.Permissions, options);
                    res.Add(item);
                }
            }
        }
        return res;
    }
}
