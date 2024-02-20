using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.API;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Security;
using System.Text.Json;

namespace Recrovit.RecroGridFramework.Client.Services;

public class ApiService : IRgfApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiService> _logger;

    public static string RgfApiClientName => "RGF.API";
    public static string RgfAuthApiClientName => "RGF.API.AUTH";
    public static string BaseAddress { get; set; } = default!;

    public ApiService(IHttpClientFactory httpClientFactory, ILogger<ApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public async Task<IRgfApiResponse<ResultType>> GetAsync<ResultType>(IRgfApiRequest request) where ResultType : class
    {
        var res = new ApiResponse<ResultType>() { Success = false };
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(request.AuthClient ? RgfAuthApiClientName : RgfApiClientName);
            var uriBuilder = new UriBuilder(new Uri(httpClient.BaseAddress!, request.Uri)) { Query = request.Query };
            _logger.LogDebug("GetAsync => uri:{uri}", uriBuilder.Uri.PathAndQuery);
            var response = await httpClient.GetAsync(uriBuilder.Uri, request.CancellationToken);
            await GetResult(request, response, res);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, null);
            res.ErrorMessage = string.IsNullOrEmpty(ex.Message) || ex.Message == "''" ? ex.GetType().Name : ex.Message;
        }
        return res;
    }

    public async Task<IRgfApiResponse<ResultType>> PostAsync<ResultType>(IRgfApiRequest request) where ResultType : class
    {
        var res = new ApiResponse<ResultType>() { Success = false };
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(request.AuthClient ? RgfAuthApiClientName : RgfApiClientName);
            var response = await httpClient.PostAsync(request.Uri, request.Content, request.CancellationToken);
            await GetResult(request, response, res);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, null);
            res.ErrorMessage = string.IsNullOrEmpty(ex.Message) || ex.Message == "''" ? ex.GetType().Name : ex.Message;
        }
        return res;
    }

    private static async Task GetResult<ResultType>(IRgfApiRequest request, HttpResponseMessage response, ApiResponse<ResultType> result) where ResultType : class
    {
        result.StatusCode = response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            object? body;
            if (typeof(ResultType) == typeof(string))
            {
                body = await response.Content.ReadAsStringAsync(request.CancellationToken) ?? "";
            }
            else if (typeof(ResultType) == typeof(Stream))
            {
                body = await response.Content.ReadAsStreamAsync();
            }
            else if (typeof(ResultType) == typeof(HttpResponseMessage))
            {
                body = response;
            }
            else
            {
                using Stream contentStream = await response.Content.ReadAsStreamAsync();
                body = await JsonSerializer.DeserializeAsync<ResultType>(contentStream, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, request.CancellationToken);
            }
            if (body is ResultType)
            {
                result.Result = (ResultType)body;
                result.Success = true;
            }
            else
            {
                result.Success = false;
            }
        }
        else
        {
            result.ErrorMessage = response.StatusCode.ToString();
        }
    }
}

public static class IRgfServiceExtension
{
    public static Task<IRgfApiResponse<ResultType>> GetResourceAsync<ResultType>(this IRgfApiService service, string name, Dictionary<string, string> query) where ResultType : class => service.GetAsync<ResultType>($"/rgf/api/resource/{name}", query);

    public static Task<IRgfApiResponse<string>> GetAboutAsync(this IRgfApiService service) => service.GetAsync<string>($"/rgf/api/AboutDialog");

    public static Task<IRgfApiResponse<RgfResult<RgfGridResult>>> GetRecroGridAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfGridResult>, RgfGridRequest>($"/rgf/api/entity/RecroGrid", param);

    public static Task<IRgfApiResponse<RgfResult<RgfFilterResult>>> GetFilterAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfFilterResult>, RgfGridRequest>($"/rgf/api/entity/Filter", param);

    public static Task<IRgfApiResponse<RgfResult<RgfFormResult>>> GetFormAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfFormResult>, RgfGridRequest>($"/rgf/api/entity/Form", param);

    public static Task<IRgfApiResponse<RgfResult<RgfFormResult>>> UpdateDataAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfFormResult>, RgfGridRequest>($"/rgf/api/entity/UpdateData", param);

    public static Task<IRgfApiResponse<RgfResult<RgfFormResult>>> DeleteDataAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfFormResult>, RgfGridRequest>($"/rgf/api/entity/DeleteData", param);

    public static Task<IRgfApiResponse<RgfResult<RgfEmptyResult>>> SaveColumnSettingsAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfEmptyResult>, RgfGridRequest>($"/rgf/api/entity/SaveColumnSettings", param);

    public static Task<IRgfApiResponse<RgfResult<RgfPredefinedFilterResult>>> SavePredefinedFilterAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfPredefinedFilterResult>, RgfGridRequest>($"/rgf/api/entity/SavePredefinedFilter", param);

    public static Task<IRgfApiResponse<RgfResult<RgfCustomFunctionResult>>> CallCustomFunctionAsync(this IRgfApiService service, RgfGridRequest param) => service.PostAsync<RgfResult<RgfCustomFunctionResult>, RgfGridRequest>($"/rgf/api/entity/CustomFunction", param);


    public static Task<IRgfApiResponse<List<RecroSecResult>>> GetPermissionsAsync(this IRgfApiService service, IEnumerable<RecroSecQuery> param) => service.PostAsync<List<RecroSecResult>, IEnumerable<RecroSecQuery>>($"/rgf/api/recrosec/Permissions", param);

    public static Task<IRgfApiResponse<RgfUserState>> GetUserStateAsync(this IRgfApiService service, Dictionary<string, string>? query = null) => service.GetAsync<RgfUserState>($"/rgf/api/recrosec/UserState", query);
}