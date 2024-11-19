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

    IRgfNotificationManager ToastManager { get; }

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

    Task<List<RgfChartSettings>> GetChartSettingsListAsync();

    Task<RgfChartSettings?> SaveChartSettingsAsync(RgfChartSettings settings, bool recreate = false);

    Task<bool> DeleteChartSettingsAsync(int chartSettingsId);

    event EventHandler<CreateGridRequestEventArgs> CreateGridRequestCreated;

    RgfGridRequest CreateGridRequest(Action<RgfGridRequest>? create = null);

    Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest request);

    Task<RgfResult<RgfGridResult>> GetAggregateDataAsync(RgfGridRequest request);

    Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(RgfGridRequest request);

    Task<ResultType> GetResourceAsync<ResultType>(string name, Dictionary<string, string> query) where ResultType : class;

    Task<bool> RecreateAsync();

    IRgFormHandler CreateFormHandler();

    Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest request);

    Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest request);

    Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey);

    Task BroadcastMessages(RgfCoreMessages messages, object sender);

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
        ToastManager = serviceProvider.GetRequiredService<IRgfEventNotificationService>().GetNotificationManager(RgfToastEvent.NotificationManagerScope);
    }

    public async Task<bool> InitializeAsync(RgfGridRequest request, bool formOnly = false)
    {
        _filterHandler = null;
        SelectParam = request.SelectParam;

        if (ListHandler == null)
        {
            ListHandler = await RgListHandler.CreateAsync(this, request);
        }
        else
        {
            await ListHandler.InitializeAsync(request);
        }

        if (ListHandler.Initialized != true)
        {
            return false;
        }

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

    public IRgfNotificationManager ToastManager { get; }

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
            var res = await _rgfService.GetFilterAsync(CreateGridRequest());
            if (!res.Success)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            }
            else
            {
                if (!res.Result.Success)
                {
                    await BroadcastMessages(res.Result.Messages, this);
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
        var request = CreateGridRequest((request) =>
        {
            request.PredefinedFilter = predefinedFilter;
        });
        var res = await _rgfService.SavePredefinedFilterAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public event EventHandler<CreateGridRequestEventArgs>? CreateGridRequestCreated;

    public RgfGridRequest CreateGridRequest(Action<RgfGridRequest>? create = null)
    {
        var request = RgfGridRequest.Create(SessionParams);
        try
        {
            create?.Invoke(request);
            var eventArgs = new CreateGridRequestEventArgs(request);
            CreateGridRequestCreated?.Invoke(this, eventArgs);
            return eventArgs.Request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateGridRequest: {EntityName}", request.EntityName);
            throw;
        }
    }

    public async Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest request)
    {
        _logger.LogDebug("GetRecroGridAsync: {EntityName}", request.EntityName);
        var res = await _rgfService.GetRecroGridAsync(request);
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

    public async Task<RgfResult<RgfGridResult>> GetAggregateDataAsync(RgfGridRequest request)
    {
        _logger.LogDebug("GetAggregateDataAsync: {EntityName}", request.EntityName);
        var res = await _rgfService.GetAggregatedDataAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(RgfGridRequest request)
    {
        var res = await _rgfService.CallCustomFunctionAsync(request);
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

    public async Task<bool> RecreateAsync()
    {
        var request = CreateGridRequest((request) =>
        {
            if (ListHandler != null && EntityDesc != null)
            {
                request.EntityName = EntityDesc.EntityName;
            }
            request.SelectParam = SelectParam;
            request.Skeleton = true;
        });
        var res = await InitializeAsync(request);
        if (res == true)
        {
            RefreshEntity.Invoke(false);
        }
        return res;
    }

    public virtual async Task<RgfGridSetting?> SaveGridSettingsAsync(RgfGridSettings settings, bool recreate = false)
    {
        var request = CreateGridRequest((request) =>
        {
            request.GridSettings = settings;
        });
        bool reset = settings.ColumnSettings == null || settings.ColumnSettings.Length == 0;
        var toast = RgfToastEvent.CreateActionEvent(_recroDict.GetRgfUiString("Request"), EntityDesc.Title, _recroDict.GetRgfUiString(reset ? "ResetSettings" : "SaveSettings"));
        await ToastManager.RaiseEventAsync(toast, this);
        var res = await _rgfService.SaveGridSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                await ToastManager.RaiseEventAsync(RgfToastEvent.RecreateToastWithStatus(toast, _recroDict.GetRgfUiString("Processed"), RgfToastType.Success), this);
            }
            if (res.Result != null && !res.Result.Success)
            {
                await BroadcastMessages(res.Result.Messages, this);
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
        var request = CreateGridRequest((request) =>
        {
            request.GridSettings = new RgfGridSettings() { GridSettingsId = gridSettingsId };
        });
        var res = await _rgfService.DeleteGridSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            return false;
        }

        GridSettingList = GridSettingList.Where(e => e.GridSettingsId != gridSettingsId).ToList();
        if (res.Result != null && !res.Result.Success)
        {
            await BroadcastMessages(res.Result.Messages, this);
        }
        return true;
    }

    public async Task<List<RgfChartSettings>> GetChartSettingsListAsync()
    {
        var res = await _rgfService.GetChartSettingsListAsync(CreateGridRequest());
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else if (res.Result != null && !res.Result.Success)
        {
            await BroadcastMessages(res.Result.Messages, this);
        }
        return res.Result?.Result ?? [];
    }

    public async Task<RgfChartSettings?> SaveChartSettingsAsync(RgfChartSettings settings, bool recreate = false)
    {
        var request = CreateGridRequest((request) =>
        {
            request.ChartSettings = settings;
            var gs = ListHandler.GetGridSettings();
            request.ChartSettings.ParentGridSettings = new RgfGridSettings()
            {
                Filter = gs.Filter,
                SQLTimeout = gs.SQLTimeout
            };
        });
        var toast = RgfToastEvent.CreateActionEvent(_recroDict.GetRgfUiString("Request"), EntityDesc.Title, _recroDict.GetRgfUiString("SaveSettings"));
        await ToastManager.RaiseEventAsync(toast, this);
        var res = await _rgfService.SaveChartSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                await ToastManager.RaiseEventAsync(RgfToastEvent.RecreateToastWithStatus(toast, _recroDict.GetRgfUiString("Processed"), RgfToastType.Success), this);
            }
            if (res.Result != null && !res.Result.Success)
            {
                await BroadcastMessages(res.Result.Messages, this);
            }
            else if (recreate)
            {
                await RecreateAsync();
            }
        }
        return res.Result?.Result;
    }

    public virtual async Task<bool> DeleteChartSettingsAsync(int chartSettingsId)
    {
        var request = CreateGridRequest((request) =>
        {
            request.ChartSettings = new RgfChartSettings() { ChartSettingsId = chartSettingsId };
        });
        var res = await _rgfService.DeleteChartSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            return false;
        }

        if (res.Result != null && !res.Result.Success)
        {
            await BroadcastMessages(res.Result.Messages, this);
        }
        return true;
    }

    #region Form

    public IRgFormHandler CreateFormHandler() => RgFormHandler.Create(this);

    public async Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest request)
    {
        var res = await _rgfService.GetFormAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest request)
    {
        request.UserColumns = ListHandler.UserColumns.ToArray();
        var res = await _rgfService.UpdateDataAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey)
    {
        var request = CreateGridRequest((request) =>
        {
            request.EntityKey = entityKey;
        });
        var res = await _rgfService.DeleteDataAsync(request);
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
            await BroadcastMessages(res.Result.Messages, this);
        }
        return res.Result;
    }

    #endregion

    public async Task BroadcastMessages(RgfCoreMessages messages, object sender)
    {
        //TODO: error handle
        if (messages != null)
        {
            if (messages.Error != null)
            {
                foreach (var item in messages.Error)
                {
                    await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Error, item.Value), sender);
                }
            }
            if (messages.Warning != null)
            {
                foreach (var item in messages.Warning)
                {
                    await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Warning, item.Value), sender);
                }
            }
            if (messages.Info != null)
            {
                foreach (var item in messages.Info)
                {
                    await NotificationManager.RaiseEventAsync(new RgfUserMessage(_recroDict, UserMessageType.Information, item.Value), sender);
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