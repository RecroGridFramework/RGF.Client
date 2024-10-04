using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Models;
using Recrovit.RecroGridFramework.Client.Services;
using System.ComponentModel;
using System.Reflection;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgManager : IDisposable
{
    RgfSessionParams SessionParams { get; }

    IServiceProvider ServiceProvider { get; }

    IRgfNotificationManager NotificationManager { get; }

    IRgListHandler ListHandler { get; }

    RgfEntity EntityDesc { get; }

    ObservableProperty<List<RgfDynamicDictionary>> SelectedItems { get; }

    ObservableProperty<FormViewKey?> FormViewKey { get; }

    RgfSelectParam? SelectParam { get; }

    ObservableProperty<int> ItemCount { get; }

    ObservableProperty<int> PageSize { get; }

    ObservableProperty<int> ActivePage { get; }

    List<RgfGridSetting> GridSettingList { get; }

    bool IsFiltered { get; }

    string EntityDomId => $"RecroGrid-{SessionParams?.GridId}";

    Task<IRgFilterHandler> GetFilterHandlerAsync();

    Task InitFilterHandlerAsync(string condition);

    Task<RgfResult<RgfPredefinedFilterResult>> SavePredefinedFilterAsync(RgfPredefinedFilter predefinedFilter);

    Task<RgfGridSetting?> SaveGridSettingsAsync(RgfGridSettings settings, bool recreate = false);

    Task<bool> DeleteGridSettingsAsync(int gridSettingsId);

    Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest param);

    Task<RgfResult<RgfChartDataResult>> GetChartDataAsync(RgfChartDataRequest param);

    Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(RgfGridRequest param);

    Task<ResultType> GetResourceAsync<ResultType>(string name, Dictionary<string, string> query) where ResultType : class;

    IRgFormHandler CreateFormHandler();

    Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest param);

    Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest param);

    Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey);

    void BroadcastMessages(RgfCoreMessages messages, object sender);

    Task OnToolbarCommandAsync(IRgfEventArgs<RgfToolbarEventArgs> arg);

    event Action<bool> RefreshEntity;
    Task<string> AboutAsync();
}

public class RgManager : IRgManager
{
    public RgManager(RgfSessionParams param, IServiceProvider serviceProvider)
    {
        SessionParams = param;
        ServiceProvider = serviceProvider;
        _rgfService = serviceProvider.GetRequiredService<IRgfApiService>();
        _logger = serviceProvider.GetRequiredService<ILogger<RgManager>>();
        _recroDict = serviceProvider.GetRequiredService<IRecroDictService>();
        _recroSec = serviceProvider.GetRequiredService<IRecroSecService>();
        NotificationManager = new RgfNotificationManager(serviceProvider);
    }

    public async Task<bool> InitializeAsync(RgfGridRequest param, bool formOnly = false)
    {
        _filterHandler = null;
        if (ListHandler != null)
        {
            ListHandler.Dispose();
        }

        SelectParam = param.SelectParam;
        ListHandler = await RgListHandler.CreateAsync(this, param);
        if (EntityDesc.Options.ContainsKey("RGO_FilterParams"))
        {
            await GetFilterHandlerAsync();
        }
        if (formOnly)
        {
            if (ListHandler.ItemCount.Value == 1)
            {
                SelectedItems.Value = await ListHandler.GetDataListAsync();
                await OnToolbarCommandAsync(new RgfEventArgs<RgfToolbarEventArgs>(this, new(RgfToolbarEventKind.Read)));
            }
            else
            {
                _logger.LogError("formOnly => ItemCount={ItemCount}", ListHandler.ItemCount.Value);
                return false;
            }
        }
        return true;
    }


    public IServiceProvider ServiceProvider { get; }

    private IRecroDictService _recroDict { get; }

    public IRecroSecService _recroSec { get; }

    public IRgfNotificationManager NotificationManager { get; }

    public IRgListHandler ListHandler { get; private set; } = default!;


    public RgfSessionParams SessionParams { get; private set; }

    public RgfEntity EntityDesc => ListHandler.EntityDesc;


    public ObservableProperty<List<RgfDynamicDictionary>> SelectedItems { get; private set; } = new(new(), nameof(SelectedItems));

    public ObservableProperty<FormViewKey?> FormViewKey { get; private set; } = new(new(), nameof(FormViewKey));

    public RgfSelectParam? SelectParam { get; private set; }


    public ObservableProperty<int> ItemCount => ListHandler.ItemCount;

    public ObservableProperty<int> PageSize => ListHandler.PageSize;

    public ObservableProperty<int> ActivePage => ListHandler.ActivePage;

    public List<RgfGridSetting> GridSettingList { get; private set; } = [];


    public bool IsFiltered => ListHandler.IsFiltered;

    public event Action<bool> RefreshEntity = default!;

    private IRgfApiService _rgfService { get; }

    private ILogger<RgManager> _logger { get; }

    private RgFilterHandler? _filterHandler { get; set; }

    public async Task<IRgFilterHandler> GetFilterHandlerAsync()
    {
        if (_filterHandler == null)
        {
            string? xmlFilter = null;
            List<RgfPredefinedFilter>? predefinedFilters = null;
            var res = await _rgfService.GetFilterAsync(new RgfGridRequest(SessionParams));
            if (!res.Success)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            }
            else
            {
                if (!res.Result.Success)
                {
                    BroadcastMessages(res.Result.Messages, this);
                }
                else
                {
                    var result = res.Result.Result;
                    xmlFilter = result.XmlFilter;
                    predefinedFilters = result.PredefinedFilter;
                }
            }
            string condition = EntityDesc.Options.GetStringValue("RGO_FilterParams");
            _filterHandler = new RgFilterHandler(this, EntityDesc, xmlFilter, condition, predefinedFilters);
            ListHandler.InitFilter(_filterHandler.StoreFilter());
        }
        return _filterHandler!;
    }

    public async Task InitFilterHandlerAsync(string condition)
    {
        if (_filterHandler != null)
        {
            _filterHandler.InitFilter(condition);
            ListHandler.InitFilter(_filterHandler.StoreFilter());
        }
        else if (!string.IsNullOrEmpty(condition))
        {
            await GetFilterHandlerAsync();
        }
    }

    public async Task<RgfResult<RgfPredefinedFilterResult>> SavePredefinedFilterAsync(RgfPredefinedFilter predefinedFilter)
    {
        RgfGridRequest param = new(SessionParams)
        {
            PredefinedFilter = predefinedFilter
        };
        var res = await _rgfService.SavePredefinedFilterAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest param)
    {
        _logger.LogDebug("GetRecroGridAsync: {EntityName}", param.EntityName);
        var res = await _rgfService.GetRecroGridAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else if (res.Result.Success)
        {
            if (SessionParams.SessionId == null)
            {
                SessionParams.SessionId = res.Result.Result.SessionId;
            }
            if (res.Result.Result?.GridId != null)
            {
                SessionParams.GridId = res.Result.Result.GridId;
            }
            if (res.Result.Result?.GridSettingList != null)
            {
                GridSettingList = res.Result.Result.GridSettingList;
            }
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfChartDataResult>> GetChartDataAsync(RgfChartDataRequest param)
    {
        _logger.LogDebug("GetChartDataAsync: {EntityName}", param.GridRequest.EntityName);
        var res = await _rgfService.GetChartDataAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(RgfGridRequest param)
    {
        var res = await _rgfService.CallCustomFunctionAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<ResultType> GetResourceAsync<ResultType>(string name, Dictionary<string, string> query) where ResultType : class
    {
        if (query == null)
        {
            query = new();
        }
        if (!query.ContainsKey("lang"))
        {
            query.Add("lang", _recroSec.UserLanguage);
        }
        var res = await _rgfService.GetResourceAsync<ResultType>(name, query);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task RecreateAsync()
    {
        RgfGridRequest param = new(SessionParams)
        {
            EntityName = EntityDesc.EntityName,
            SelectParam = SelectParam,
            Skeleton = true
        };
        await InitializeAsync(param);
        RefreshEntity.Invoke(false);
    }

    public virtual async Task<RgfGridSetting?> SaveGridSettingsAsync(RgfGridSettings settings, bool recreate = false)
    {
        RgfGridRequest param = new(SessionParams)
        {
            GridSettings = settings
        };
        var res = await _rgfService.SaveGridSettingsAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result != null && !res.Result.Success)
            {
                BroadcastMessages(res.Result.Messages, this);
            }
            else if (recreate)
            {
                await RecreateAsync();
            }
        }
        return res.Result?.Result;
    }

    public virtual async Task<bool> DeleteGridSettingsAsync(int gridSettingsId)
    {
        RgfGridRequest param = new(SessionParams)
        {
            GridSettings = new RgfGridSettings() { GridSettingsId = gridSettingsId }
        };
        var res = await _rgfService.DeleteGridSettingsAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            return false;
        }
        else
        {
            GridSettingList = GridSettingList.Where(e => e.GridSettingsId != gridSettingsId).ToList();
            if (res.Result != null && !res.Result.Success)
            {
                BroadcastMessages(res.Result.Messages, this);
            }
            return true;
        }
    }

    #region Form

    public IRgFormHandler CreateFormHandler() => RgFormHandler.Create(this);

    public async Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest param)
    {
        var res = await _rgfService.GetFormAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest param)
    {
        param.UserColumns = ListHandler.UserColumns.ToArray();
        var res = await _rgfService.UpdateDataAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey)
    {
        RgfGridRequest param = new(SessionParams)
        {
            EntityKey = entityKey
        };
        var res = await _rgfService.DeleteDataAsync(param);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                await ListHandler.DeleteRowAsync(entityKey);
            }
            BroadcastMessages(res.Result.Messages, this);
        }
        return res.Result;
    }

    #endregion

    public void BroadcastMessages(RgfCoreMessages messages, object sender)
    {
        //TODO: error handle
        if (messages != null)
        {
            if (messages.Error != null)
            {
                foreach (var item in messages.Error)
                {
                    _ = NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, item.Value), sender);
                }
            }
            if (messages.Warning != null)
            {
                foreach (var item in messages.Warning)
                {
                    _ = NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Warning, item.Value), sender);
                }
            }
            if (messages.Info != null)
            {
                foreach (var item in messages.Info)
                {
                    _ = NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Information, item.Value), sender);
                }
            }
        }
    }

    public virtual async Task OnToolbarCommandAsync(IRgfEventArgs<RgfToolbarEventArgs> arg)
    {
        _logger.LogDebug("OnToolbarCommand: {cmd}", arg.Args.EventKind);
        switch (arg.Args.EventKind)
        {
            case RgfToolbarEventKind.Refresh:
                await ListHandler.RefreshDataAsync();
                break;

            case RgfToolbarEventKind.Add:
                if (ListHandler.CRUD.Add && EntityDesc.Options.GetBoolValue("RGO_NoDetails") != true)
                {
                    FormViewKey.Value = new(new RgfEntityKey());
                }
                break;

            case RgfToolbarEventKind.Edit:
            case RgfToolbarEventKind.Read:
                if ((ListHandler.CRUD.Read || ListHandler.CRUD.Edit) && EntityDesc.Options.GetBoolValue("RGO_NoDetails") != true)
                {
                    var data = SelectedItems.Value.SingleOrDefault();
                    if (data != null && ListHandler.GetEntityKey(data, out var entityKey))
                    {
                        FormViewKey.Value = new(entityKey!, ListHandler.GetAbsoluteRowIndex(data));
                    }
                }
                break;

            case RgfToolbarEventKind.Delete:
                if (ListHandler.CRUD.Delete)
                {
                    var data = SelectedItems.Value.SingleOrDefault();
                    if (data != null && ListHandler.GetEntityKey(data, out var entityKey) && entityKey != null)
                    {
                        await DeleteDataAsync(entityKey);
                    }
                }
                break;

            case RgfToolbarEventKind.Select:
                OnSelect();
                break;
        }
    }

    protected virtual void OnSelect()
    {
        if (SelectParam != null && SelectedItems.Value.Count == 1)
        {
            var data = SelectedItems.Value.Single();
            if (ListHandler.GetEntityKey(data, out var entityKey) && entityKey != null)
            {
                _logger.LogDebug("OnSelect: {key}", entityKey.Keys.FirstOrDefault().Value);
                SelectParam.SelectedKey = entityKey;
            }
            if (SelectParam.Filter.Keys.Any())
            {
                var parClientName = SelectParam.Filter.Keys.First().Key;
                var prop = EntityDesc.Properties.FirstOrDefault(e => e.Options?.Any(o => o.Key.Equals("ParClientName") && o.Value.ToString() == parClientName) == true);
                if (prop != null)
                {
                    SelectParam.Filter.Keys[parClientName] = data.GetMember(prop.Alias);
                }
            }
            SelectParam.ItemSelectedEvent.InvokeAsync(new CancelEventArgs());
        }
    }

    public async Task<string> AboutAsync()
    {
        var res = await _rgfService.GetAboutAsync();
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        string about = res.Result ?? "";
        if (!string.IsNullOrEmpty(about))
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Recrovit.RecroGridFramework.Client.Blazor.UI");
            if (assembly != null)
            {
                var ver = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                about = about.Replace("<div class=\"client-ver\"></div>", $"<div class=\"client-ver\">RecroGrid Framework Blazor.UI v{ver}</div>");
            }
        }
        return about;
    }

    public void Dispose()
    {
        if (ListHandler != null)
        {
            ListHandler.Dispose();
            ListHandler = null!;
        }
    }
}