using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Models;
using Recrovit.RecroGridFramework.Client.Services;
using System.ComponentModel;
using System.Reflection;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public enum RfgDisplayMode
{
    Grid = 1,
    Tree = 2
}

public interface IRgManager : IDisposable
{
    RgfSessionParams SessionParams { get; }

    IServiceProvider ServiceProvider { get; }

    IRgfNotificationManager NotificationManager { get; }

    IRgfNotificationManager ToastManager { get; }

    IRgListHandler ListHandler { get; }

    RgfEntity EntityDesc { get; }

    ObservableProperty<Dictionary<int, RgfEntityKey>> SelectedItems { get; }

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

    bool IsColumnFiltered(IRgfProperty property, string? matchCriteria = null);

    Task<RgfResult<RgfFilterSetting>> SaveFilterSettingsAsync(RgfFilterSettings predefinedFilter);

    Task<bool> DeleteFilterSettingsAsync(int filterSettingsId);

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

    Task<RgfPropertyTooltips> GetPropertyTooltipsAsync();

    Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest request);

    Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey);

    Task<int> DeleteSelectedItemsAsync();

    Task BroadcastMessages(RgfCoreMessages messages, object sender);

    Task OnToolbarCommandAsync(IRgfEventArgs<RgfToolbarEventArgs> arg);

    event Action<bool> RefreshEntity;

    Task<string> AboutAsync();
}

public static class IRgManagerExtensions
{
    public static List<RgfDynamicDictionary> GetSelectedRowsData(this IRgManager manager) => manager.ListHandler.GetSelectedRowsData(manager.SelectedItems.Value);

    public static bool ValidFormKeyExists(this IRgManager manager) => manager.FormViewKey?.Value?.EntityKey.IsEmpty == false;
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

        var notificationService = serviceProvider.GetRequiredService<IRgfEventNotificationService>();
        NotificationManager = notificationService.GetNotificationManager(NotificationManagerScope);
        ToastManager = notificationService.GetNotificationManager(RgfToastEventArgs.NotificationManagerScope);
    }

    public async Task<bool> InitializeAsync(RgfGridRequest request)
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

        await VersionCompatibilityAsync();

        return true;
    }

    public IServiceProvider ServiceProvider { get; }

    private const string NotificationManagerScope = "CoreNotificationManager";

    public IRgfNotificationManager NotificationManager { get; }

    public IRgfNotificationManager ToastManager { get; }

    public IRgListHandler ListHandler { get; private set; } = default!;


    public RgfSessionParams SessionParams { get; private set; }

    public RgfEntity EntityDesc => ListHandler.EntityDesc;


    public ObservableProperty<Dictionary<int, RgfEntityKey>> SelectedItems { get; private set; } = new(new(), nameof(SelectedItems));

    public ObservableProperty<FormViewKey?> FormViewKey { get; private set; } = new(new(), nameof(FormViewKey));

    public RgfSelectParam? SelectParam { get; private set; }


    public ObservableProperty<int> ItemCount => ListHandler.ItemCount;

    public ObservableProperty<int> PageSize => ListHandler.PageSize;

    public ObservableProperty<int> ActivePage => ListHandler.ActivePage;

    public List<RgfGridSetting> GridSettingList { get; private set; } = [];

    public bool IsFiltered => ListHandler.IsFiltered;

    public event Action<bool> RefreshEntity = default!;

    private IRgfApiService _rgfService;

    private ILogger<RgManager> _logger;

    private IRecroDictService _recroDict;

    private IRecroSecService _recroSec;

    private static Version? CurrentRgfCoreVersion;

    private RgFilterHandler? _filterHandler { get; set; }

    private RgfPropertyTooltips? _propertyTooltips;

    public async Task<IRgFilterHandler> GetFilterHandlerAsync()
    {
        if (_filterHandler == null)
        {
            string? xmlFilter = null;
            List<RgfFilterSettings>? filterSettings = null;
            var res = await _rgfService.GetFilterAsync(CreateGridRequest());
            if (!res.Success)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
                    filterSettings = result.FilterSettings;
                }
            }
            string condition = EntityDesc.Options.GetStringValue("RGO_FilterParams");
            _filterHandler = new RgFilterHandler(this, EntityDesc, xmlFilter, condition, filterSettings);
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

    public bool IsColumnFiltered(IRgfProperty property, string? matchCriteria = null) => _filterHandler?.IsColumnFiltered(property, matchCriteria) == true;

    public async Task<RgfResult<RgfFilterSetting>> SaveFilterSettingsAsync(RgfFilterSettings filterSettings)
    {
        var request = CreateGridRequest((request) =>
        {
            request.FilterSettings = filterSettings;
        });
        var res = await _rgfService.SaveFilterSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<bool> DeleteFilterSettingsAsync(int filterSettingsId)
    {
        var request = CreateGridRequest((request) =>
        {
            request.FilterSettings = new() { FilterSettingsId = filterSettingsId };
        });
        var res = await _rgfService.DeleteFilterSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
            return false;
        }
        return true;
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(RgfGridRequest request)
    {
        var res = await _rgfService.CallCustomFunctionAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
        var toast = RgfToastEventArgs.CreateActionEvent(_recroDict.GetRgfUiString("Request"), EntityDesc.Title, _recroDict.GetRgfUiString(reset ? "ResetSettings" : "SaveSettings"));
        await ToastManager.RaiseEventAsync(toast, this);
        var res = await _rgfService.SaveGridSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                await ToastManager.RaiseEventAsync(toast.RecreateAsSuccess(_recroDict.GetRgfUiString("Processed")), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
                Conditions = gs.Conditions,
                SQLTimeout = gs.SQLTimeout
            };
        });
        var toast = RgfToastEventArgs.CreateActionEvent(_recroDict.GetRgfUiString("Request"), EntityDesc.Title, _recroDict.GetRgfUiString("SaveSettings"));
        await ToastManager.RaiseEventAsync(toast, this);
        var res = await _rgfService.SaveChartSettingsAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                await ToastManager.RaiseEventAsync(toast.RecreateAsSuccess(_recroDict.GetRgfUiString("Processed")), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfPropertyTooltips> GetPropertyTooltipsAsync() => _propertyTooltips ??= await LoadPropertyTooltipsAsync();

    private async Task<RgfPropertyTooltips> LoadPropertyTooltipsAsync()
    {
        var res = await GetResourceAsync<string>("RecroGrid.xml", new Dictionary<string, string>() {
            { "t", "tt" },
            { "e", EntityDesc.EntityId.ToString() },
            { "lang", _recroSec.UserLanguage }
        });
        if (!string.IsNullOrEmpty(res) && RgfPropertyTooltips.Deserialize(res, out var tooltips))
        {
            return tooltips;
        }
        return new RgfPropertyTooltips() { EntityId = EntityDesc.EntityId };
    }

    public virtual async Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest request)
    {
        request.UserColumns = ListHandler.UserColumns.ToArray();
        var res = await _rgfService.UpdateDataAsync(request);
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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

    public virtual async Task<int> DeleteSelectedItemsAsync()
    {
        var items = SelectedItems.Value.OrderBy(e => e.Key).ToArray();
        int count = 0;
        foreach (var item in items)
        {
            var res = await DeleteDataAsync(item.Value);
            if (!res.Success)
            {
                break;
            }
            await SelectedItems.ModifySilentlyAsync(SelectedItems.Value.Where(e => e.Key != item.Key).ToDictionary(e => e.Key - 1, e => e.Value));
            count++;
        }
        if (count > 0)
        {
            if (count == items.Count())
            {
                var msg = string.Format(_recroDict.GetRgfUiString("DelSuccess"), count);
                var toast = RgfToastEventArgs.CreateActionEvent(_recroDict.GetRgfUiString("Delete"), EntityDesc.Title, msg, RgfToastType.Success);
                await ToastManager.RaiseEventAsync(toast, this);
            }
            else
            {
                var messages = new RgfCoreMessages();
                var msg = string.Format(_recroDict.GetRgfUiString("DelIncomplete"), count, items.Count() - count);
                messages.Error = new() { { "BulkDelete", msg } };
                await BroadcastMessages(messages, this);
            }
            await ListHandler.RefreshDataAsync();
        }
        return count;
    }

    #endregion

    public async Task BroadcastMessages(RgfCoreMessages messages, object sender)
    {
        if (messages == null)
        {
            return;
        }

        if (messages.Info != null)
        {
            foreach (var item in messages.Info)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Information, item.Value.Replace("\r\n", "<br/>")), sender);
            }
        }

        if (messages.Warning != null)
        {
            foreach (var item in messages.Warning)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Warning, item.Value.Replace("\r\n", "<br/>")), sender);
            }
        }

        if (messages.Error != null)
        {
            foreach (var item in messages.Error)
            {
                await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, item.Value.Replace("\r\n", "<br/>")), sender);
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
                {
                    FormViewKey? key = null;
                    if (arg.Args.Data != null && ListHandler.GetEntityKey(arg.Args.Data, out var entityKey) && entityKey != null)
                    {
                        var idx = ListHandler.GetAbsoluteRowIndex(arg.Args.Data);
                        key = new(entityKey, idx);
                    }
                    if (key == null && SelectedItems.Value.Count == 1 && (ListHandler.CRUD.Read || ListHandler.CRUD.Edit) && EntityDesc.Options.GetBoolValue("RGO_NoDetails") != true)
                    {
                        var data = SelectedItems.Value.SingleOrDefault();
                        key = new(data.Value, data.Key);
                    }
                    if (key?.EntityKey.IsEmpty == false)
                    {
                        FormViewKey.Value = key;
                    }
                }
                break;

            case RgfToolbarEventKind.Delete:
                if (ListHandler.CRUD.Delete && arg.Args.Data != null)
                {
                    if (ListHandler.GetEntityKey(arg.Args.Data, out var key) && key?.IsEmpty == false)
                    {
                        await DeleteDataAsync(key);
                        SelectedItems.Value = new();
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
            if (data.Value?.IsEmpty == false)
            {
                _logger.LogDebug("OnSelect: {key}", data.Value.Keys.FirstOrDefault().Value);
                SelectParam.SelectedKeys = [data.Value];
            }
            if (SelectParam.Filter.Keys.Any())
            {
                var rowData = ListHandler.GetRowData(data.Key);
                if (rowData != null)
                {
                    var parClientName = SelectParam.Filter.Keys.First().Key;
                    var prop = EntityDesc.Properties.FirstOrDefault(e => e.Options?.Any(o => o.Key.Equals("ParClientName") && o.Value.ToString() == parClientName) == true);
                    if (prop != null)
                    {
                        SelectParam.Filter.Keys[parClientName] = rowData.GetMember(prop.Alias);
                    }
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
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
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

    private async Task VersionCompatibilityAsync()
    {
        if (CurrentRgfCoreVersion != null)
        {
            return;
        }

        var res = await _rgfService.VersionCompatibilityAsync();
        if (!res.Success)
        {
            await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(_recroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Result.TryGetValue(RgfHeaderKeys.RgfCoreVersion, out var version) &&
                Version.TryParse(version, out CurrentRgfCoreVersion) &&
                CurrentRgfCoreVersion < RgfClientConfiguration.MinimumRgfCoreVersion)
            {
                const string incompatibilityMessageTemplate =
                    $"The RGF Server Core is running version {{0}}, but the current RGF Client Blazor requires at least version {{1}}.\r\n Please update the RGF Server Core to avoid unexpected behavior.";

                _logger.LogWarning(incompatibilityMessageTemplate.Replace("\r\n", ""), CurrentRgfCoreVersion, RgfClientConfiguration.MinimumRgfCoreVersion);
                await NotificationManager.RaiseEventAsync(new RgfUserMessageEventArgs(
                    UserMessageType.Warning,
                    string.Format(incompatibilityMessageTemplate.Replace("\r\n", "<br/>"),
                    CurrentRgfCoreVersion,
                    RgfClientConfiguration.MinimumRgfCoreVersion), "Incompatible RGF version detected"), this);
            }
            await BroadcastMessages(res.Result.Messages, this);
        }
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