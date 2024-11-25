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
    bool Initialized { get; }

    ObservableProperty<int> ActivePage { get; }

    RgfEntity EntityDesc { get; }

    BasePermissions CRUD { get; }

    ObservableProperty<List<RgfDynamicDictionary>> ListDataSource { get; }

    string[] DataColumns { get; }

    bool IsFiltered { get; }

    ObservableProperty<bool> IsLoading { get; }

    ObservableProperty<int> ItemCount { get; }

    ObservableProperty<int> PageSize { get; }

    string? QueryString { get; }

    int? SQLTimeout { get; }

    Task<bool> InitializeAsync(RgfGridRequest param);

    void Dispose();

    Task<List<RgfDynamicDictionary>> GetDataListAsync(int? gridSettingsId = null);

    Task<List<RgfDynamicDictionary>> GetDataRangeAsync(int startIndex, int endIndex);

    Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(string functionName, bool requireQueryParams = false, Dictionary<string, object>? customParams = null, RgfEntityKey? entityKey = null);

    RgfGridRequest CreateAggregateRequest(RgfAggregationSettings chartParam);

    RgfDynamicDictionary GetEKey(RgfDynamicDictionary rowData);

    bool GetEntityKey(RgfDynamicDictionary rowData, out RgfEntityKey? entityKey);

    int GetAbsoluteRowIndex(RgfDynamicDictionary rowData);

    int GetRelativeRowIndex(RgfDynamicDictionary rowData);

    void InitFilter(RgfFilter.Condition[] conditions);

    Task PageChangingAsync(ObservablePropertyEventArgs<int> args);

    Task RefreshDataAsync(int? gridSettingsId = null);

    Task RefreshRowAsync(RgfDynamicDictionary rowData);

    Task AddRowAsync(RgfDynamicDictionary rowData);

    Task DeleteRowAsync(RgfEntityKey entityKey);

    Task SetFilterAsync(RgfFilter.Condition[] conditions, int? queryTimeout);

    Task<bool> SetSortAsync(Dictionary<string, int> sort);

    Task<bool> SetVisibleColumnsAsync(IEnumerable<GridColumnSettings> columnSettings);

    void ReplaceColumnWidth(int index, int width);

    void ReplaceColumnWidth(string alias, int width);

    Task MoveColumnAsync(int oldIndex, int newIndex, bool refresh = true);

    IEnumerable<int> UserColumns { get; }

    RgfGridSettings GetGridSettings();

    Task<RgfDynamicDictionary?> EnsureVisibleAsync(int index);

    RgfDynamicDictionary? GetRowData(int index);
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
        _recroDict = manager.ServiceProvider.GetRequiredService<IRecroDictService>();
    }

    public static Task<RgListHandler> CreateAsync(IRgManager manager, string entityName) => CreateAsync(manager, manager.CreateGridRequest((request) => request.EntityName = entityName));

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

    public bool Initialized { get; private set; } = false;

    public ObservableProperty<int> ItemCount { get; private set; } = new(-1, nameof(ItemCount));

    public ObservableProperty<int> PageSize { get; private set; } = new(0, nameof(PageSize));

    public string? QueryString { get; private set; }

    public int? SQLTimeout => ListParam.SQLTimeout;

    public ObservableProperty<int> ActivePage { get; private set; } = new(1, nameof(ActivePage));

    public string[] DataColumns => _dataColumns.ToArray();

    public bool IsFiltered => ListParam.UserFilter?.Any() == true;

    public ObservableProperty<bool> IsLoading { get; private set; } = new(false, nameof(ListDataSource));

    public IEnumerable<int> UserColumns => EntityDesc.SortedVisibleColumns.Select(e => e.Id);

    public ObservableProperty<List<RgfDynamicDictionary>> ListDataSource { get; private set; } = new(new List<RgfDynamicDictionary>(), nameof(ListDataSource));

    public async Task<List<RgfDynamicDictionary>> GetDataListAsync(int? gridSettingsId = null)
    {
        var list = await _getDataListAsync(ListParam, gridSettingsId);
        await ListDataSource.SetValueAsync(list);
        return list;
    }
    private async Task<List<RgfDynamicDictionary>> _getDataListAsync(RgfListParam listParam, int? gridSettingsId = null)
    {
        try
        {
            IsLoading.Value = true;
            var list = new List<RgfDynamicDictionary>();
            if (Initialized)
            {
                int page = PageSize.Value > 0 ? listParam.Skip / PageSize.Value : 0;
                if (!TryGetCacheData(page, out list))
                {
                    listParam.Columns = UserColumns.ToArray();
                    var param = _manager.CreateGridRequest((request) =>
                    {
                        request.EntityName = EntityDesc.EntityName;
                        if (gridSettingsId != null)
                        {
                            request.GridSettings = new RgfGridSettings() { GridSettingsId = gridSettingsId };
                            request.ListParam = new RgfListParam() { Reset = true };
                            request.Skeleton = true;
                        }
                        else
                        {
                            request.ListParam = listParam;
                            request.ListParam.Preload = PageSize.Value;// TODO: does not handle backward paging, so we only read 1 page
                        }
                    });
                    await LoadRecroGridAsync(param, page, gridSettingsId != null);
                    if (gridSettingsId != null)
                    {
                        await _manager.InitFilterHandlerAsync(EntityDesc.Options.GetStringValue("RGO_FilterParams"));
                    }
                    TryGetCacheData(page, out list);
                }
            }
            return list;
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    public async Task<List<RgfDynamicDictionary>> GetDataRangeAsync(int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex > ItemCount.Value || PageSize.Value == 0)
        {
            return [];
        }
        var list = new List<RgfDynamicDictionary>();
        int startPage = startIndex / PageSize.Value;
        int endPage = endIndex / PageSize.Value;
        var listParam = ListParam.ShallowCopy();
        for (int i = startPage; i <= endPage; i++)
        {
            listParam.Skip = i * PageSize.Value;
            var data = await _getDataListAsync(listParam);

            int pageStartIdx = (i == startPage) ? startIndex % PageSize.Value : 0;
            int pageEndIdx = (i == endPage) ? (endIndex % PageSize.Value) + 1 : PageSize.Value;

            list.AddRange(data.Skip(pageStartIdx).Take(pageEndIdx - pageStartIdx));
        }
        return list;
    }

    public async Task RefreshDataAsync(int? gridSettingsId = null)
    {
        await _manager.ToastManager.RaiseEventAsync(new RgfToastEvent(EntityDesc.MenuTitle, _recroDict.GetRgfUiString("Refresh"), delay: 2000), this);
        ClearCache();
        if (ActivePage.Value == 1)
        {
            await GetDataListAsync(gridSettingsId);
        }
        else
        {
            ActivePage.Value = 1;
        }
    }

    public async Task<RgfResult<RgfCustomFunctionResult>> CallCustomFunctionAsync(string functionName, bool requireQueryParams = false, Dictionary<string, object>? customParams = null, RgfEntityKey? entityKey = null)
    {
        var param = _manager.CreateGridRequest((request) =>
        {
            request.EntityName = EntityDesc.EntityName;
            request.EntityKey = entityKey;
            request.FunctionName = functionName;
            if (requireQueryParams)
            {
                request.ListParam = ListParam;
            }
            if (customParams != null)
            {
                request.CustomParams = customParams;
            }
        });
        return await _manager.CallCustomFunctionAsync(param);
    }

    public RgfGridRequest CreateAggregateRequest(RgfAggregationSettings aggregateParam)
    {
        var listParam = ListParam.ShallowCopy();
        listParam.AggregationSettings = aggregateParam;
        return _manager.CreateGridRequest((request) =>
        {
            request.EntityName = EntityDesc.EntityName;
            request.ListParam = listParam;
        });
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
        ArgumentNullException.ThrowIfNull(data);
        RgfDynamicDictionary ekey = new();
        var props = EntityDesc.Properties.Where(e => e.IsKey).ToArray();
        var keys = props.Select(e => e.ClientName).ToArray();
        for (int i = 0; i < _dataColumns.Length; i++)
        {
            var clientName = _dataColumns[i];
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

    public int GetAbsoluteRowIndex(RgfDynamicDictionary rowData)
    {
        int idx = -1;
        if (rowData != null)
        {
            var rgparams = rowData.Get<Dictionary<string, object>>("__rgparams");
            if (rgparams?.TryGetValue("rowIndex", out var rowIndex) == true)
            {
                int.TryParse(rowIndex.ToString(), out idx);
            }
        }
        return idx;
    }

    public int GetRelativeRowIndex(RgfDynamicDictionary rowData)
    {
        int idx = GetAbsoluteRowIndex(rowData);
        if (idx != -1)
        {
            idx -= (ActivePage.Value - 1) * PageSize.Value;
        }
        return idx;
    }

    public async Task<RgfDynamicDictionary?> EnsureVisibleAsync(int index)
    {
        int idx = index >= 0 && index < ItemCount.Value ? index : throw new ArgumentOutOfRangeException(nameof(index));
        int first = (ActivePage.Value - 1) * PageSize.Value;
        if (idx < first || idx >= first + PageSize.Value)
        {
            _logger.LogDebug("EnsureVisibleAsync: {index}", index);
            int page = idx / PageSize.Value + 1;
            await ActivePage.SetValueAsync(page);
            first = (ActivePage.Value - 1) * PageSize.Value;
        }
        return ListDataSource.Value[index - first];
    }

    public RgfDynamicDictionary? GetRowData(int index)
    {
        int first = (ActivePage.Value - 1) * PageSize.Value;
        if (index >= first && index < first + PageSize.Value)
        {
            return ListDataSource.Value[index - first];
        }
        return null;
    }

    public RgfGridSettings GetGridSettings()
    {
        RgfGridSettings settings = new()
        {
            ColumnSettings = EntityDesc.SortedVisibleColumns.Select(e => new RgfColumnSettings(e)).ToArray(),
            Filter = ListParam.UserFilter,
            Sort = ListParam.Sort,
            PageSize = PageSize.Value,
            SQLTimeout = ListParam.SQLTimeout
        };
        return settings;
    }

    private readonly ILogger _logger;
    private readonly IRgManager _manager;
    private readonly IRecroDictService _recroDict;
    private RgfEntity? _entityDesc;

    private DataCache _dataCache { get; set; } = new DataCache(0);

    private int[] SelectedItems { get; set; } = [];

    private Dictionary<string, object> Options { get; set; } = [];

    private RgfListParam ListParam { get; set; } = new();

    private string[] _dataColumns { get; set; } = [];

    private int QuerySkip { get; set; }

    private List<IDisposable> _disposables { get; set; } = [];

    public async Task<bool> InitializeAsync(RgfGridRequest param)
    {
        if (_disposables.Count == 0)
        {
            _disposables.Add(PageSize.OnAfterChange(this, PageSizeChanging));
            _disposables.Add(ActivePage.OnAfterChange(this, PageChangingAsync));
        }
        Initialized = false;
        bool res = await LoadRecroGridAsync(param, 0, true);
        if (res)
        {
            await GetDataListAsync();
        }
        return res;
    }

    public async Task AddRowAsync(RgfDynamicDictionary rowData)
    {
        RgfDynamicDictionary ekey = GetEKey(rowData);
        if (ekey.Count == 0 || !_dataCache.TryGetData(0, out var pageData) || pageData == null)
        {
            return;
        }
        object[][] newArray = new object[pageData.Length + 1][];
        Array.Copy(pageData, 0, newArray, 1, pageData.Length);
        newArray[0] = new object[_dataColumns.Length];
        for (int col = 0; col < _dataColumns.Length; col++)
        {
            var clientName = _dataColumns[col];
            newArray[0][col] = rowData.GetMember(clientName);
        }
        _dataCache.Clear();
        _dataCache.AddOrReplaceMultiple(0, newArray);
        ItemCount.Value++;
        if (ActivePage.Value == 1)
        {
            await GetDataListAsync();
        }
        else
        {
            ActivePage.Value = 1;
        }
    }

    public async Task RefreshRowAsync(RgfDynamicDictionary rowData)
    {
        RgfDynamicDictionary ekey = GetEKey(rowData);
        if (ekey.Count == 0)
        {
            return;
        }
        var page = ActivePage.Value - 1;
        if (_dataCache.TryGetData(page, out var pageData) && pageData != null)
        {
            bool refresh = false;
            var logger = _manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>();
            for (int index = 0; index < pageData.Length; index++)
            {
                var row2 = RgfDynamicDictionary.Create(logger, EntityDesc, _dataColumns, pageData[index], true);
                var ekey2 = GetEKey(row2);
                if (ekey.Equals(ekey2))
                {
                    for (int col = 0; col < _dataColumns.Length; col++)
                    {
                        var clientName = _dataColumns[col];
                        //pageData[i][col] = newRow.ContainsKey(clientName) ? newRow.GetMember(clientName) : "?";
                        pageData[index][col] = rowData.GetMember(clientName);
                    }
                    _dataCache.Replace(page, pageData);
                    refresh = true;
                    break;
                }
            }
            if (refresh)
            {
                await GetDataListAsync();
            }
        }
    }

    public async Task DeleteRowAsync(RgfEntityKey entityKey)
    {
        RgfDynamicDictionary key = entityKey.Keys;
        if (key.Count == 0)
        {
            return;
        }
        var page = ActivePage.Value - 1;
        if (_dataCache.TryGetData(page, out var pageData) && pageData != null)
        {
            bool refresh = false;
            var logger = _manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>();
            for (int index = 0; index < pageData.Length; index++)
            {
                var row2 = RgfDynamicDictionary.Create(logger, EntityDesc, _dataColumns, pageData[index], true);
                var ekey2 = GetEKey(row2);
                if (key.Equals(ekey2))
                {
                    _dataCache.RemovePages(page, int.MaxValue);
                    ItemCount.Value--;
                    refresh = true;
                    break;
                }
            }
            if (refresh)
            {
                await GetDataListAsync();
            }
        }
    }

    private async Task<bool> LoadRecroGridAsync(RgfGridRequest param, int page, bool init = false)
    {
        if (!init && EntityDesc.Options.GetBoolValue("RGO_ClientMode"))
        {
            await _manager.ToastManager.RaiseEventAsync(new RgfToastEvent(EntityDesc.MenuTitle, _recroDict.GetRgfUiString("InvalidOperation"), RgfToastType.Info), this);
            return false;
        }
        try
        {
            IsLoading.Value = true;
            var result = await _manager.GetRecroGridAsync(param);
            if (result != null)
            {
                await _manager.BroadcastMessages(result.Messages, this);
            }
            if (result?.Success != true)
            {
                ItemCount.Value = 0;
                Initialized = false;
                return false;
            }

            var rgResult = result.Result;
            if (rgResult.EntityDesc != null)
            {
                EntityDesc = rgResult.EntityDesc;
                ListParam.SQLTimeout = EntityDesc.Options.TryGetIntValue("RGO_SQLTimeout");
            }
            if (init)
            {
                PageSize.Value = EntityDesc.ItemsPerPage;// Math.Min(MaxItem, ItemsPerPage);
                ListParam.Take = PageSize.Value;
                ListParam.Skip = 0;
                ListParam.UserFilter = [];
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
                _dataColumns = rgResult.DataColumns;
            }
            SelectedItems = rgResult.SelectedItems;

            if (rgResult.Data != null)
            {
                _dataCache.AddOrReplaceMultiple(page, rgResult.Data);
                ListParam.Count = null;
            }
            Initialized = true;
            return true;
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    private bool TryGetCacheData(int page, out List<RgfDynamicDictionary> list)
    {
        list = new List<RgfDynamicDictionary>();
        if (_dataCache.TryGetData(page, out var pageData) && pageData != null)
        {
            var logger = _manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>();
            int idx = this.PageSize.Value * page;
            foreach (var item in pageData)
            {
                var rowData = RgfDynamicDictionary.Create(logger, EntityDesc, _dataColumns, item, true);
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