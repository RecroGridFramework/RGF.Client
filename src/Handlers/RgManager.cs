﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Services;
using System.ComponentModel;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgManager : IDisposable
{
    RgfSessionParams SessionParams { get; }

    IServiceProvider ServiceProvider { get; }
    IRecroDictService RecroDict { get; }
    IRecroSecService RecroSec { get; }
    IRgfNotificationManager NotificationManager { get; }
    IRgListHandler ListHandler { get; }

    RgfEntity EntityDesc { get; }

    ObservableProperty<List<RgfDynamicDictionary>> SelectedItems { get; }
    ObservableProperty<RgfEntityKey?> FormDataKey { get; }
    RgfSelectParam? SelectParam { get; }

    ObservableProperty<int> ItemCount { get; }
    ObservableProperty<int> PageSize { get; }
    ObservableProperty<int> ActivePage { get; }
    bool IsFiltered { get; }
    string EntityDomId => $"RecroGrid-{SessionParams?.GridId}";

    Task<IRgFilterHandler> GetFilterHandlerAsync();
    Task<RgfResult<RgfPredefinedFilterResult>> SavePredefinedFilterAsync(RgfPredefinedFilter predefinedFilter);

    Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest param);

    IRgFormHandler CreateFormHandler();
    Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest param);
    Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest param);
    Task<RgfResult<RgfFormResult>> DeleteDataAsync(RgfEntityKey entityKey);

    void BroadcastMessages(RgfMessages messages, object sender);

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
        RecroDict = serviceProvider.GetRequiredService<IRecroDictService>();
        RecroSec = serviceProvider.GetRequiredService<IRecroSecService>();
        NotificationManager = new RgfNotificationManager(serviceProvider);

        _disposables.Add(NotificationManager.Subscribe<RgfToolbarEventArgs>(this, OnToolbarCommandAsync));
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
                await OnToolbarCommandAsync(new RgfEventArgs<RgfToolbarEventArgs>(this, new(ToolbarAction.Read)));
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
    public IRecroDictService RecroDict { get; }
    public IRecroSecService RecroSec { get; }
    public IRgfNotificationManager NotificationManager { get; }
    public IRgListHandler ListHandler { get; private set; } = default!;

    public RgfSessionParams SessionParams { get; private set; }
    public RgfEntity EntityDesc => ListHandler.EntityDesc;

    public ObservableProperty<List<RgfDynamicDictionary>> SelectedItems { get; private set; } = new(new(), nameof(SelectedItems));
    public ObservableProperty<RgfEntityKey?> FormDataKey { get; private set; } = new(new(), nameof(FormDataKey));
    public RgfSelectParam? SelectParam { get; private set; }

    public ObservableProperty<int> ItemCount => ListHandler.ItemCount;
    public ObservableProperty<int> PageSize => ListHandler.PageSize;
    public ObservableProperty<int> ActivePage => ListHandler.ActivePage;
    public bool IsFiltered => ListHandler.IsFiltered;

    public event Action<bool> RefreshEntity = default!;

    private IRgfApiService _rgfService { get; }
    private ILogger<RgManager> _logger { get; }

    private List<IDisposable> _disposables { get; set; } = new();

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
                NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
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

    public async Task<RgfResult<RgfPredefinedFilterResult>> SavePredefinedFilterAsync(RgfPredefinedFilter predefinedFilter)
    {
        RgfGridRequest param = new(SessionParams)
        {
            PredefinedFilter = predefinedFilter
        };
        var res = await _rgfService.SavePredefinedFilterAsync(param);
        if (!res.Success)
        {
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public async Task<RgfResult<RgfGridResult>> GetRecroGridAsync(RgfGridRequest param)
    {
        var res = await _rgfService.GetRecroGridAsync(param);
        if (!res.Success)
        {
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
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

    protected virtual async Task SaveColumnSettingsAsync(RgfGridSettings settings, bool recreate = false)
    {
        RgfGridRequest param = new(SessionParams)
        {
            GridSettings = settings
        };
        var res = await _rgfService.SaveColumnSettingsAsync(param);
        if (!res.Success)
        {
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
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
    }

    #region Form

    public IRgFormHandler CreateFormHandler() => RgFormHandler.Create(this);

    public async Task<RgfResult<RgfFormResult>> GetFormAsync(RgfGridRequest param)
    {
        var res = await _rgfService.GetFormAsync(param);
        if (!res.Success)
        {
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result;
    }

    public virtual async Task<RgfResult<RgfFormResult>> UpdateFormDataAsync(RgfGridRequest param)
    {
        param.UserColumns = ListHandler.UserColumns.ToArray();
        var res = await _rgfService.UpdateDataAsync(param);
        if (!res.Success)
        {
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
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
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        else
        {
            if (res.Result.Success)
            {
                NotificationManager.RaiseEvent(new RgfListViewEventArgs(ListViewAction.DeleteRow, entityKey.Keys), this);
            }
            BroadcastMessages(res.Result.Messages, this);
        }
        return res.Result;
    }

    #endregion

    public void BroadcastMessages(RgfMessages messages, object sender)
    {
        //TODO: error handle
        if (messages != null)
        {
            if (messages.Error != null)
            {
                foreach (var item in messages.Error)
                {
                    NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, item.Value), sender);
                }
            }
            if (messages.Warning != null)
            {
                foreach (var item in messages.Warning)
                {
                    NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Warning, item.Value), sender);
                }
            }
            if (messages.Info != null)
            {
                foreach (var item in messages.Info)
                {
                    NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Information, item.Value), sender);
                }
            }
        }
    }

    protected virtual async Task OnToolbarCommandAsync(IRgfEventArgs<RgfToolbarEventArgs> args)
    {
        _logger.LogDebug("OnToolbarCommand: {cmd}", args.Args.Command);
        switch (args.Args.Command)
        {
            case ToolbarAction.Refresh:
                await ListHandler.RefreshDataAsync();
                break;

            case ToolbarAction.ShowFilter:
                break;

            case ToolbarAction.Add:
                if (ListHandler.CRUD.Add && EntityDesc.Options.GetBoolValue("RGO_NoDetails") != true)
                {
                    FormDataKey.Value = new RgfEntityKey();
                }
                break;

            case ToolbarAction.Edit:
            case ToolbarAction.Read:
                if ((ListHandler.CRUD.Read || ListHandler.CRUD.Edit) && EntityDesc.Options.GetBoolValue("RGO_NoDetails") != true)
                {
                    var data = SelectedItems.Value.SingleOrDefault();
                    if (data != null && ListHandler.GetEntityKey(data, out var entityKey))
                    {
                        FormDataKey.Value = entityKey;
                    }
                }
                break;

            case ToolbarAction.Delete:
                if (ListHandler.CRUD.Delete)
                {
                    var data = SelectedItems.Value.SingleOrDefault();
                    if (data != null && ListHandler.GetEntityKey(data, out var entityKey) && entityKey != null)
                    {
                        await DeleteDataAsync(entityKey);
                    }
                }
                break;

            case ToolbarAction.Select:
                OnSelect();
                break;

            case ToolbarAction.SaveSettings:
                await SaveColumnSettingsAsync(ListHandler.GetGridSettings());
                break;

            case ToolbarAction.ResetSettings:
                await SaveColumnSettingsAsync(new RgfGridSettings(), true);
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
            NotificationManager.RaiseEvent(new RgfUserMessage(RecroDict, UserMessageType.Error, res.ErrorMessage), this);
        }
        return res.Result ?? "";
    }

    public void Dispose()
    {
        if (_disposables != null)
        {
            _disposables.ForEach(disposable => disposable.Dispose());
            _disposables = null!;
        }
        if (ListHandler != null)
        {
            ListHandler.Dispose();
            ListHandler = null!;
        }
    }
}
