using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Security;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using System.Data;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgListHandler
{
    ObservableProperty<int> ActivePage { get; }
    RgfEntity EntityDesc { get; }
    BasePermissions CRUD { get; }

    ObservableProperty<List<RgfDynamicDictionary>> ListDataSource { get; }
    bool IsFiltered { get; }
    bool IsLoading { get; }

    ObservableProperty<int> ItemCount { get; }
    int ItemsPerPage { get; }
    ObservableProperty<int> PageSize { get; }
    string? QueryString { get; }

    void Dispose();

    Task<List<RgfDynamicDictionary>> GetDataListAsync();

    Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(string functionName, bool requireQueryParams = false, Dictionary<string, object>? customParams = null, RgfEntityKey? entityKey = null);

    RgfDynamicDictionary GetEKey(RgfDynamicDictionary rowData);

    bool GetEntityKey(RgfDynamicDictionary rowData, out RgfEntityKey? entityKey);

    int GetRowIndex(RgfDynamicDictionary rowData);

    void InitFilter(RgfFilter.Condition[] conditions);

    Task PageChangingAsync(ObservablePropertyEventArgs<int> args);

    Task RefreshDataAsync();

    Task SetFilterAsync(RgfFilter.Condition[] conditions, int? queryTimeout);

    Task<bool> SetSortAsync(Dictionary<string, int> sort);

    Task<bool> SetVisibleColumnsAsync(IEnumerable<GridColumnSettings> columnSettings);

    void ReplaceColumnWidth(int index, int width);

    void ReplaceColumnWidth(string alias, int width);

    Task MoveColumnAsync(int oldIndex, int newIndex, bool refresh = true);

    IEnumerable<int> UserColumns { get; }

    RgfGridSettings GetGridSettings();
}

internal class RgListHandler : IDisposable, IRgListHandler
{
    private class DataCache
    {
        public DataCache(int pageSize)
        {
            PageSize = pageSize;
        }
        private int PageSize { get; set; } = 0;
        private Dictionary<int, object[][]> _data { get; } = new Dictionary<int, object[][]>();

        public bool TryGetData(int page, out object[][]? data) => _data.TryGetValue(page, out data);

        public void Replace(int page, object[][] data) => _data[page] = data;
        public void AddOrReplaceMultiple(int page, object[][] data)
        {
            if (PageSize > 0)
            {
                if (data.Length == 0)
                {
                    _data[page] = data;
                }
                else
                {
                    int pageCount = (int)Math.Ceiling((double)data.Length / PageSize);
                    for (int i = 0; i < pageCount; i++)
                    {
                        if (!_data.ContainsKey(i + page))
                        {
                            int startIndex = i * PageSize;
                            int minSize = Math.Min(PageSize, data.Length - startIndex);
                            var p = new object[minSize][];
                            Array.Copy(data, startIndex, p, 0, minSize);
                            _data[i + page] = p;
                        }
                    }
                }
            }
        }

        public void RemovePage(int start) => RemovePages(start, start);
        public void RemovePages(int start, int end)
        {
            foreach (int page in _data.Keys.Where(e => e >= start && e <= end).ToArray())
            {
                _data.Remove(page);
            }
        }

        public void Clear() => _data.Clear();
    }

    private RgListHandler(ILogger<RgListHandler> logger, IRgManager manager)
    {
        _logger = logger;
        _manager = manager;
    }

    public static Task<RgListHandler> CreateAsync(IRgManager manager, string entityName) => CreateAsync(manager, new RgfGridRequest() { EntityName = entityName });

    public static async Task<RgListHandler> CreateAsync(IRgManager manager, RgfGridRequest param)
    {
        var logger = manager.ServiceProvider.GetRequiredService<ILogger<RgListHandler>>();
        var handler = new RgListHandler(logger, manager);
        await handler.InitializeAsync(param);
        return handler;
    }

    public RgfEntity EntityDesc
    {
        get => _entityDesc ?? new();
        private set
        {
            _entityDesc = value;
            CRUD = new RgfPermissions(_entityDesc.CRUD).BasePermissions;
        }
    }

    public BasePermissions CRUD { get; private set; }

    public ObservableProperty<int> ItemCount { get; private set; } = new(-1, nameof(ItemCount));
    public ObservableProperty<int> PageSize { get; private set; } = new(0, nameof(PageSize));
    public ObservableProperty<int> ActivePage { get; private set; } = new(1, nameof(ActivePage));
    public int ItemsPerPage => (int)EntityDesc.Options.GetLongValue("RGO_ItemsPerPage", 10);
    public bool IsFiltered => ListParam.UserFilter?.Any() == true;
    public bool IsLoading { get; set; }

    public ObservableProperty<List<RgfDynamicDictionary>> ListDataSource { get; private set; } = new(new List<RgfDynamicDictionary>(), nameof(ListDataSource));

    public async Task<List<RgfDynamicDictionary>> GetDataListAsync()
    {
        IsLoading = true;
        List<RgfDynamicDictionary> list = new();
        try
        {
            if (_initialized)
            {
                int page = PageSize.Value > 0 ? ListParam.Skip / PageSize.Value : 0;
                if (!TryGetCacheData(page, out list))
                {
                    ListParam.Columns = UserColumns.ToArray();
                    var param = new RgfGridRequest(_manager.SessionParams)
                    {
                        EntityName = EntityDesc.EntityName,
                        ListParam = ListParam
                    };
                    param.ListParam.Preload = PageSize.Value;//TODO: nem kezeli a visszafelé lapozást, ezért csak 1 lapot olvasunk
                    await LoadRecroGridAsync(param, page);
                    TryGetCacheData(page, out list);
                }
            }
            ListDataSource.Value = list;
        }
        finally
        {
            IsLoading = false;
        }
        return list;
    }

    public async Task RefreshDataAsync()
    {
        ClearCache();
        if (ActivePage.Value == 1)
        {
            await GetDataListAsync();
        }
        else
        {
            ActivePage.Value = 1;
        }
    }

    public async Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(string functionName, bool requireQueryParams = false, Dictionary<string, object>? customParams = null, RgfEntityKey? entityKey = null)
    {
        var param = new RgfGridRequest(_manager.SessionParams)
        {
            EntityName = _manager.EntityDesc.EntityName,
            SessionId = _manager.SessionParams.SessionId,
            EntityKey = entityKey
        };
        param.FunctionName = functionName;
        if (requireQueryParams)
        {
            param.ListParam = ListParam;
        }
        if (customParams != null)
        {
            param.CustomParams = customParams;
        }

        return await _manager.CallCustomFunctionAsync(param);
    }

    public Task PageChangingAsync(ObservablePropertyEventArgs<int> args)
    {
        ListParam.Skip = (args.NewData - 1) * PageSize.Value;
        return GetDataListAsync();
    }

    public async Task<bool> SetSortAsync(Dictionary<string, int> sort)
    {
        bool changed = false;
        ListParam.Sort = sort.Select(e => new int[] { EntityDesc.Properties.First(p => p.Alias == e.Key).Id, e.Value }).ToArray();
        ListParam.Skip = 0;
        foreach (var item in EntityDesc.Properties)
        {
            if (sort.TryGetValue(item.Alias, out int s))
            {
                if (item.Sort != s)
                {
                    item.Sort = s;
                    changed = true;
                }
            }
            else if (item.Sort != 0)
            {
                item.Sort = 0;
                changed = true;
            }
        }
        if (changed)
        {
            await RefreshDataAsync();
        }
        return changed;
    }

    public async Task<bool> SetVisibleColumnsAsync(IEnumerable<GridColumnSettings> columnSettings)
    {
        bool reload = false;
        bool changed = false;
        foreach (var col in columnSettings)
        {
            var prop = EntityDesc.Properties.SingleOrDefault(e => e.Id == col.Property.Id);
            if (prop != null)
            {
                var pos = col.ColPos ?? 0;
                if (prop.ColPos != pos)
                {
                    changed = true;
                    if (prop.ColPos == 0)
                    {
                        reload = true;
                    }
                    prop.ColPos = pos;
                }
                var width = col.ColWidth ?? 0;
                if (prop.ColWidth != width)
                {
                    changed = true;
                    prop.ColWidth = width;
                }
            }
        }
        int idx = 1;
        foreach (var item in EntityDesc.SortedVisibleColumns)
        {
            item.ColPos = idx++;
        }
        if (reload)
        {
            await RefreshDataAsync();
        }
        else if (changed)
        {
            await GetDataListAsync();
        }
        return changed || reload;
    }

    public void ReplaceColumnWidth(int index, int width)
    {
        int idx = 1;
        foreach (var col in EntityDesc.SortedVisibleColumns)
        {
            if (idx == index)
            {
                ReplaceColumnWidth(col.Alias, width);
                break;
            }
            idx++;
        }
    }

    public void ReplaceColumnWidth(string alias, int width)
    {
        _logger.LogDebug("ReplaceColumnWidth: {alias}:{width}", alias, width);
        var col = EntityDesc.Properties.SingleOrDefault(x => x.Alias == alias);
        if (col != null)
        {
            col.ColWidth = width;
        }
    }

    public async Task MoveColumnAsync(int oldIndex, int newIndex, bool refresh = true)
    {
        _logger.LogDebug("MoveColumn: {old}->{new}", oldIndex, newIndex);
        var list = EntityDesc.SortedVisibleColumns.ToList();
        if (oldIndex != newIndex &&
            oldIndex > 0 && oldIndex <= list.Count &&
            newIndex > 0 && newIndex <= list.Count)
        {
            var item = list[oldIndex - 1];
            list.RemoveAt(oldIndex - 1);
            list.Insert(newIndex - 1, item);
            for (int i = 0; i < list.Count;)
            {
                list[i].ColPos = ++i;
            }
            ListParam.Columns = UserColumns.ToArray();
            if (refresh)
            {
                await GetDataListAsync();
            }
        }
    }

    public void InitFilter(RgfFilter.Condition[] conditions)
    {
        ListParam.UserFilter = conditions;
    }

    public async Task SetFilterAsync(RgfFilter.Condition[] conditions, int? queryTimeout)
    {
        InitFilter(conditions);
        ListParam.SQLTimeout = queryTimeout;
        await RefreshDataAsync();
    }

    public RgfDynamicDictionary GetEKey(RgfDynamicDictionary data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        RgfDynamicDictionary ekey = new();
        var props = EntityDesc.Properties.Where(e => e.IsKey).ToArray();
        var keys = props.Select(e => e.ClientName).ToArray();
        for (int i = 0; i < DataColumns.Length; i++)
        {
            var clientName = DataColumns[i];
            if (keys.Contains(clientName))
            {
                object? val;
                if (data.TryGetMember(clientName, out val) && val != null)
                {
                    ekey.Add(clientName, val);
                }
                else
                {
                    var prop = props.SingleOrDefault(e => e.ClientName == clientName);
                    if (!string.IsNullOrEmpty(prop?.Alias)
                        && data.TryGetMember(prop.Alias, out val) && val != null)
                    {
                        ekey.Add(clientName, val);
                    }
                }
            }
        }
        return ekey;
    }

    public bool GetEntityKey(RgfDynamicDictionary rowData, out RgfEntityKey? entityKey)
    {
        var rgparams = rowData.Get<Dictionary<string, object>>("__rgparams");
        if (rgparams?.TryGetValue("keySign", out var k) == true)
        {
            entityKey = new RgfEntityKey() { Keys = GetEKey(rowData), Signature = k.ToString() };
            return true;
        }
        entityKey = null;
        return false;
    }

    public int GetRowIndex(RgfDynamicDictionary rowData)
    {
        int idx = -1;
        var rgparams = rowData.Get<Dictionary<string, object>>("__rgparams");
        if (rgparams?.TryGetValue("rowIndex", out var rowIndex) == true)
        {
            int.TryParse(rowIndex.ToString(), out idx);
        }
        return idx;
    }

    public RgfGridSettings GetGridSettings()
    {
        RgfGridSettings settings = new()
        {
            ColumnSettings = EntityDesc.SortedVisibleColumns.Select(e => new RgfColumnSettings()
            {
                Id = e.Id,
                ColPos = e.ColPos,
                ColWidth = e.ColWidth
            }).ToArray(),
            Filter = ListParam.UserFilter,
            Sort = ListParam.Sort,
            PageSize = PageSize.Value,
        };
        return settings;
    }

    private readonly ILogger<RgListHandler> _logger;
    private readonly IRgManager _manager;
    private RgfEntity? _entityDesc;
    private bool _initialized = false;

    private DataCache _dataCache { get; set; } = new DataCache(0);

    private int[] SelectedItems { get; set; } = [];

    private Dictionary<string, object> Options { get; set; } = new();

    private RgfListParam ListParam { get; set; } = new();

    private string[] DataColumns { get; set; } = new string[0];

    public IEnumerable<int> UserColumns => EntityDesc.SortedVisibleColumns.Select(e => e.Id);

    private int QuerySkip { get; set; }

    public string? QueryString { get; private set; }

    private List<IDisposable> _disposables { get; set; } = new();

    private async Task InitializeAsync(RgfGridRequest param)
    {
        _disposables.Add(_manager.NotificationManager.Subscribe<RgfListViewEventArgs>(this, OnListViewEvent));
        _disposables.Add(PageSize.OnAfterChange(this, PageSizeChanging));
        _disposables.Add(ActivePage.OnAfterChange(this, PageChangingAsync));

        await LoadRecroGridAsync(param, 0, true);
        await GetDataListAsync();
    }

    private async Task OnListViewEvent(IRgfEventArgs<RgfListViewEventArgs> args)
    {
        _logger.LogDebug("OnListViewEvent: {cmd}", args.Args.Command);

        RgfDynamicDictionary? newRow = null;
        RgfDynamicDictionary ekey;
        if (args.Args.Command == ListViewAction.DeleteRow)
        {
            ekey = args.Args.Data;
        }
        else
        {
            newRow = args.Args.Data;
            ekey = GetEKey(newRow);
        }
        if (!ekey.Any())
        {
            return;
        }
        var page = args.Args.Command == ListViewAction.AddRow ? 0 : ActivePage.Value - 1;
        if (_dataCache.TryGetData(page, out var pageData) && pageData != null)
        {
            bool refresh = false;
            if (args.Args.Command == ListViewAction.AddRow && newRow != null)
            {
                object[][] newArray = new object[pageData.Length + 1][];
                Array.Copy(pageData, 0, newArray, 1, pageData.Length);
                newArray[0] = new object[DataColumns.Length];
                for (int col = 0; col < DataColumns.Length; col++)
                {
                    var clientName = DataColumns[col];
                    newArray[0][col] = newRow.GetMember(clientName);
                }
                _dataCache.Clear();
                _dataCache.AddOrReplaceMultiple(0, newArray);
                ItemCount.Value++;
                if (ActivePage.Value == 1)
                {
                    refresh = true;
                }
                else
                {
                    ActivePage.Value = 1;
                }
            }
            else
            {
                var logger = _manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>();
                for (int index = 0; index < pageData.Length; index++)
                {
                    var row2 = RgfDynamicDictionary.Create(logger, EntityDesc, DataColumns, pageData[index], true);
                    var ekey2 = GetEKey(row2);
                    if (ekey.Equals(ekey2))
                    {
                        if (args.Args.Command == ListViewAction.DeleteRow)
                        {
                            _dataCache.RemovePages(page, int.MaxValue);
                            ItemCount.Value--;
                        }
                        else if (args.Args.Command == ListViewAction.RefreshRow && newRow != null)
                        {
                            for (int col = 0; col < DataColumns.Length; col++)
                            {
                                var clientName = DataColumns[col];
                                //pageData[i][col] = newRow.ContainsKey(clientName) ? newRow.GetMember(clientName) : "?";
                                pageData[index][col] = newRow.GetMember(clientName);
                            }
                            _dataCache.Replace(page, pageData);
                        }
                        refresh = true;
                        break;
                    }
                }
            }
            if (refresh)
            {
                await GetDataListAsync();
            }
        }
    }

    private async Task LoadRecroGridAsync(RgfGridRequest param, int page, bool init = false)
    {
        var result = await _manager.GetRecroGridAsync(param);
        if (result != null)
        {
            _manager.BroadcastMessages(result.Messages, this);
        }
        if (result?.Success != true)
        {
            ItemCount.Value = 0;
            return;
        }

        var rgResult = result.Result;
        if (rgResult.EntityDesc != null)
        {
            EntityDesc = rgResult.EntityDesc;
        }
        if (init)
        {
            PageSize.Value = ItemsPerPage;// Math.Min(MaxItem, ItemsPerPage);
            ListParam.Take = PageSize.Value;
            ListParam.Skip = 0;
            ListParam.UserFilter = new RgfFilter.Condition[0];
            ListParam.Sort = EntityDesc.SortColumns.Select(e => new[] { e.Id, e.Sort }).ToArray();
            ClearCache();
        }
        if (rgResult.Options != null)
        {
            Options = rgResult.Options;
            QuerySkip = (int)Options.GetLongValue("RGO_QuerySkip", QuerySkip);
            ItemCount.Value = (int)Options.GetLongValue("RGO_MaxItem", ItemCount.Value);
            QueryString = Options.GetStringValue("RGO_QueryString");
        }

        if (rgResult.DataColumns != null)
        {
            DataColumns = rgResult.DataColumns;
        }
        SelectedItems = rgResult.SelectedItems;

        if (rgResult.Data != null)
        {
            _dataCache.AddOrReplaceMultiple(page, rgResult.Data);
            ListParam.Count = null;
        }
        _initialized = true;
    }

    private bool TryGetCacheData(int page, out List<RgfDynamicDictionary> list)
    {
        list = new List<RgfDynamicDictionary>();
        if (_dataCache.TryGetData(page, out var pageData) && pageData != null)
        {
            var logger = _manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>();
            int idx = this.PageSize.Value * page + 1;
            foreach (var item in pageData)
            {
                var rowData = RgfDynamicDictionary.Create(logger, EntityDesc, DataColumns, item, true);
                var rgparams = rowData.GetOrNew<Dictionary<string, object>>("__rgparams");
                rgparams["rowIndex"] = idx++;
                list.Add(rowData);
            }
            return true;
        }
        return false;
    }

    private void PageSizeChanging(ObservablePropertyEventArgs<int> args)
    {
        if (args.OrigData != args.NewData)
        {
            ListParam.Take = args.NewData;
            ListParam.Skip = 0;
            ClearCache();
        }
    }

    private void ClearCache()
    {
        _dataCache = new DataCache(PageSize.Value);
        ListParam.Count = true;
    }

    public void Dispose()
    {
        if (_disposables != null)
        {
            _disposables.ForEach(disposable => disposable.Dispose());
            _disposables = null!;
        }
    }
}