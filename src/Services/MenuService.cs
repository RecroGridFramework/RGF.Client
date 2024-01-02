using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.API;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class MenuService : IRgfMenuService
{
    private readonly IRgfApiService _apiService;
    private readonly IRecroSecService _recroSec;

    public MenuService(IRgfApiService apiService, IRecroSecService recroSec)
    {
        _apiService = apiService;
        _recroSec = recroSec;
    }

    public virtual Task<IRgfApiResponse<List<RgfMenu>>> GetMenuAsync(int menuId, string lang, string? scope = null)
        => _apiService.GetAsync<List<RgfMenu>>($"/rgf/api/Menu/{menuId}/{(string.IsNullOrEmpty(lang) ? "eng" : lang)}/{scope ?? string.Empty}");
}
