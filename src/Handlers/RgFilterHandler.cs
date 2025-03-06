using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Models;
using System.Text.Json;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgFilterHandler
{
    List<RgfFilter.Condition> Conditions { get; }

    List<RgfFilterSettings> PredefinedFilters { get; }

    RgfFilterProperty[] RgfFilterProperties { get; }

    bool IsColumnFiltered(IRgfProperty property, string? matchCriteria = null);

    Task SetQuickFilterAsync(IRgfProperty property, object? condition);

    int FindCondition(IList<RgfFilter.Condition> conditions, int clientId, out RgfFilter.Condition condition);

    void AddBracket(int clientId);

    RgfFilter.Condition? AddCondition(ILogger logger, int clientId);

    bool ChangeProperty(RgfFilter.Condition condition, int newPropertyId);

    bool ChangeQueryOperator(ILogger logger, RgfFilter.Condition condition, RgfFilter.QueryOperator newOperator);

    bool InitFilter(string? jsonCondition);

    void RemoveBracket(int clientId);

    void RemoveCondition(int clientId);

    bool ResetFilter();

    Task SetFilterAsync(IEnumerable<RgfFilter.Condition>? conditions, int? sqlTimeout);

    RgfFilterSettings? SelectPredefinedFilter(int? filterSettingsId);

    Task<bool> SaveFilterSettingsAsync(RgfFilterSettings filterSettings);

    Task<bool> DeleteFilterSettingsAsync(int filterSettingsId);

    RgfFilter.Condition[] StoreFilter();
}

internal class RgFilterHandler : IRgFilterHandler
{
    public RgFilterHandler(IRgManager manager, RgfEntity entity, string? xmlfilter = null, string? jsonConditions = null, List<RgfFilterSettings>? predefinedFilters = null)
    {
        _manager = manager;
        _entity = entity;
        if (string.IsNullOrEmpty(xmlfilter) || !RgfFilter.Deserialize(xmlfilter, out _filter))
        {
            _filter = new RgfFilter();
        }
        InitFilter(jsonConditions);
        PredefinedFilters = predefinedFilters ?? new List<RgfFilterSettings>();
    }

    private readonly RgfEntity _entity;
    private RgfFilter _filter;
    private RgfFilterProperty[]? _rgfFilterProperties = null;
    private string _jsonConditions = string.Empty;
    private readonly IRgManager _manager;
    private List<RgfFilter.Condition> _conditions = [];
    private List<RgfFilter.Condition> _conditionsQuickFilter = [];
    private int _maxConditionId;

    public RgfFilterProperty[] RgfFilterProperties
    {
        get
        {
            if (_rgfFilterProperties == null)
            {
                _rgfFilterProperties = _entity.Properties
                    .Join(_filter.Columns, prop => prop.Alias, col => col.Alias, (prop, col) => new RgfFilterProperty(prop, col), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(e => e.ColTitle)
                    .ToArray();
            }
            return _rgfFilterProperties;
        }
    }

    public List<RgfFilter.Condition> Conditions
    {
        get
        {
            if (_conditionsQuickFilter.Count == 0)
            {
                return _conditions;
            }

            var list = new List<RgfFilter.Condition>
            {
                new RgfFilter.Condition() { LogicalOperator = RgfFilter.LogicalOperator.And, Conditions = _conditions },
                new RgfFilter.Condition() { LogicalOperator = RgfFilter.LogicalOperator.And, Conditions = _conditionsQuickFilter, IsQuickFilter = true }
            };
            return list;
        }
        set
        {
            _conditions = value;
            _conditionsQuickFilter = [];
            _maxConditionId = InitClientId(new RgfFilter.Condition() { Conditions = _conditions }, 0);
        }
    }

    public List<RgfFilterSettings> PredefinedFilters { get; set; }

    public bool IsColumnFiltered(IRgfProperty property, string? matchCriteria = null) => _manager.ListHandler.IsFiltered && IsColumnFilteredRecursive(Conditions, property, matchCriteria);

    private bool IsColumnFilteredRecursive(IEnumerable<RgfFilter.Condition> conditions, IRgfProperty property, string? matchCriteria)
    {
        foreach (var item in conditions)
        {
            if (item.PropertyId == property.Id &&
                (matchCriteria == null || item.IsQuickFilter && matchCriteria.Equals(item.Param1.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (item.Conditions != null && IsColumnFilteredRecursive(item.Conditions, property, matchCriteria))
            {
                return true;
            }
        }
        return false;
    }

    public async Task SetQuickFilterAsync(IRgfProperty property, object? condition)
    {
        if (string.IsNullOrWhiteSpace(condition?.ToString()))
        {
            condition = null;
        }
        var quickFilter = _conditionsQuickFilter.FirstOrDefault(e => e.IsQuickFilter && e.PropertyId == property.Id);
        if (quickFilter == null && condition == null)
        {
            return;
        }

        if (quickFilter != null && condition == null)
        {
            _conditionsQuickFilter.Remove(quickFilter);
        }
        else
        {
            if (quickFilter == null)
            {
                quickFilter = new RgfFilter.Condition()
                {
                    ClientId = ++_maxConditionId,
                    PropertyId = property.Id,
                    LogicalOperator = RgfFilter.LogicalOperator.And,
                    QueryOperator = property.ClientDataType == ClientDataType.String ? RgfFilter.QueryOperator.Like : RgfFilter.QueryOperator.Equal,
                    IsQuickFilter = true
                };
                _conditionsQuickFilter.Add(quickFilter);
            }
            quickFilter.Param1 = condition?.ToString();
        }

        await _manager.ListHandler.SetFilterAsync(StoreFilter(), _manager.ListHandler.SQLTimeout);
    }

    public bool ResetFilter() => InitFilter(_jsonConditions);

    public bool InitFilter(string? jsonConditions)
    {
        if (string.IsNullOrEmpty(jsonConditions))
        {
            _jsonConditions = string.Empty;
            Conditions = [];
            return true;
        }
        try
        {
            var conds = JsonSerializer.Deserialize<List<RgfFilter.Condition>>(jsonConditions, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            if (conds != null)
            {
                _jsonConditions = jsonConditions;
                Conditions = conds;
                return true;
            }
        }
        catch { }
        return false;
    }

    public RgfFilter.Condition[] StoreFilter()
    {
        _jsonConditions = JsonSerializer.Serialize(Conditions, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        return Conditions.ToArray();
    }

    private int InitClientId(RgfFilter.Condition condition, int prevId)
    {
        int maxId = prevId + 1;
        condition.ClientId = maxId;
        if (condition.Conditions != null)
        {
            foreach (var item in condition.Conditions)
            {
                maxId = InitClientId(item, maxId);
            }
        }
        return maxId;
    }

    #region Edit
    public void RemoveCondition(int clientId) => RemoveCondition(Conditions, clientId);

    private bool RemoveCondition(IList<RgfFilter.Condition> conditions, int clientId)
    {
        if (clientId == 0)
        {
            return false;
        }
        var condition = conditions.SingleOrDefault(e => e.ClientId == clientId);
        if (condition != null)
        {
            conditions.Remove(condition);
            return true;
        }
        foreach (var item in conditions.Where(e => e.Conditions != null))
        {
            if (RemoveCondition(item.Conditions, clientId))
            {
                return true;
            }
        }
        return false;
    }

    public void AddBracket(int clientId)
    {
        int idx = FindCondition(Conditions, clientId, out var condition);
        if (idx != -1)
        {
            var newBracket = new RgfFilter.Condition()
            {
                ClientId = ++_maxConditionId,
                LogicalOperator = RgfFilter.LogicalOperator.And,
                Conditions = new List<RgfFilter.Condition>()
            };
            newBracket.Conditions.Add(condition);
            if (FindParentCondition(Conditions, clientId, out var parent))
            {
                parent.Conditions.Insert(idx, newBracket);
            }
            else
            {
                Conditions.Insert(idx, newBracket);
            }
            RemoveCondition(clientId);
        }
    }

    public void RemoveBracket(int clientId)
    {
        if (clientId == 0)
        {
            return;
        }
        int idx = FindCondition(Conditions, clientId, out var condition);
        if (idx != -1)
        {
            if (FindParentCondition(Conditions, clientId, out var parent))
            {
                parent.Conditions.InsertRange(idx, condition.Conditions);
            }
            else
            {
                Conditions.InsertRange(idx, condition.Conditions);
            }
            RemoveCondition(clientId);
        }
    }

    public RgfFilter.Condition? AddCondition(ILogger logger, int clientId)
    {
        RgfFilter.Condition? newCondition = null;
        var prop = RgfFilterProperties.FirstOrDefault();
        if (prop != null)
        {
            newCondition = new RgfFilter.Condition()
            {
                ClientId = ++_maxConditionId,
                PropertyId = prop.Id,
                LogicalOperator = RgfFilter.LogicalOperator.And,
                QueryOperator = prop.Operators.First(),
            };

            newCondition.Param1 = GetDefaultValue(newCondition, prop);
            logger.LogDebug("AddCondition: {ColTitle}", prop.ColTitle);

            int idx = FindCondition(Conditions, clientId, out var condition);
            if (idx != -1)
            {
                condition.Conditions.Add(newCondition);
            }
            else
            {
                Conditions.Add(newCondition);
            }
        }
        return newCondition;
    }

    public int FindCondition(IList<RgfFilter.Condition> conditions, int clientId, out RgfFilter.Condition condition)
    {
        foreach (var item in conditions)
        {
            if (item.ClientId == clientId)
            {
                condition = item;
                return conditions.IndexOf(condition);
            }
            if (item.Conditions != null)
            {
                int idx = FindCondition(item.Conditions, clientId, out condition);
                if (idx != -1)
                {
                    return idx;
                }
            }
        }
        condition = new RgfFilter.Condition();
        return -1;
    }

    private bool FindParentCondition(IList<RgfFilter.Condition> conditions, int clientId, out RgfFilter.Condition condition)
    {
        foreach (var item in conditions)
        {
            if (item.Conditions != null)
            {
                if (item.Conditions.Any(e => e.ClientId == clientId))
                {
                    condition = item;
                    return true;
                }
                else
                {
                    if (FindParentCondition(item.Conditions, clientId, out condition))
                    {
                        return true;
                    }
                }
            }
        }
        condition = new RgfFilter.Condition();
        return false;
    }

    public bool ChangeProperty(RgfFilter.Condition condition, int newPropertyId)
    {
        var prop = RgfFilterProperties.FirstOrDefault(e => e.Id == newPropertyId);
        if (prop != null && condition.PropertyId != newPropertyId)
        {
            condition.PropertyId = newPropertyId;
            if (!prop.Operators.Contains(condition.QueryOperator))
            {
                condition.QueryOperator = prop.Operators.First();
            }
            condition.Param1 = GetDefaultValue(condition, prop);
            condition.Param2 = null;
            return true;
        }
        return false;
    }

    public bool ChangeQueryOperator(ILogger logger, RgfFilter.Condition condition, RgfFilter.QueryOperator newOperator)
    {
        if (newOperator != RgfFilter.QueryOperator.Invalid && newOperator != condition.QueryOperator)
        {
            var prop = RgfFilterProperties.FirstOrDefault(e => e.Id == condition.PropertyId);
            if (prop != null && prop.Operators.Contains(newOperator))
            {
                //var listTypes = new RgfFilter.QueryOperator[] { RgfFilter.QueryOperator.In, RgfFilter.QueryOperator.NotIn };
                var intervalTypes = new RgfFilter.QueryOperator[] { RgfFilter.QueryOperator.Interval, RgfFilter.QueryOperator.IntervalE };
                var nullTypes = new RgfFilter.QueryOperator[] { RgfFilter.QueryOperator.IsNull, RgfFilter.QueryOperator.IsNotNull };

                if (nullTypes.Contains(newOperator))
                {
                    condition.Param1 = null;
                }
                if (!intervalTypes.Contains(newOperator) || !intervalTypes.Contains(condition.QueryOperator))
                {
                    condition.Param2 = null;
                }
                if (condition.Param1 == null)
                {
                    condition.Param1 = GetDefaultValue(condition, prop);
                }
                logger.LogDebug("ChangeQueryOperator: {QueryOperator}", newOperator);
                condition.QueryOperator = newOperator;
                return true;
            }
        }
        return false;
    }

    private static object? GetDefaultValue(RgfFilter.Condition condition, RgfFilterProperty property)
    {
        return property.ClientDataType switch
        {
            ClientDataType.String => condition.QueryOperator switch
            {
                RgfFilter.QueryOperator.Equal or
                RgfFilter.QueryOperator.NotEqual or
                RgfFilter.QueryOperator.Like or
                RgfFilter.QueryOperator.NotLike or
                RgfFilter.QueryOperator.Interval or
                RgfFilter.QueryOperator.IntervalE => string.Empty,
                _ => null
            },
            ClientDataType.Boolean => false,
            ClientDataType.DateTime => DateTime.Today,
            _ => null
        };
    }

    public async Task SetFilterAsync(IEnumerable<RgfFilter.Condition>? conditions, int? sqlTimeout)
    {
        Conditions = conditions?.ToList() ?? [];
        await _manager.ListHandler.SetFilterAsync([.. Conditions], sqlTimeout);
    }

    public RgfFilterSettings? SelectPredefinedFilter(int? filterSettingsId)
    {
        var filter = PredefinedFilters.SingleOrDefault(e => e.FilterSettingsId == filterSettingsId);
        if (filter != null)
        {
            filter = RgfFilterSettings.DeepCopy(filter);
            Conditions = filter.Conditions.ToList();
        }
        return filter;
    }

    public async Task<bool> SaveFilterSettingsAsync(RgfFilterSettings filter)
    {
        if (string.IsNullOrWhiteSpace(filter.SettingsName))
        {
            return false;
        }

        filter.Conditions = Conditions.ToArray();
        var res = await _manager.SaveFilterSettingsAsync(filter);
        if (!res.Success)
        {
            await _manager.BroadcastMessages(res.Messages, this);
            return false;
        }
        if (filter.FilterSettingsId == null || filter.FilterSettingsId == 0)
        {
            filter.FilterSettingsId = res.Result.FilterSettingsId;
        }
        //We need a new list for the ComboBox DataSource to refresh.
        PredefinedFilters = PredefinedFilters.Where(e => e.FilterSettingsId != filter.FilterSettingsId).ToList();
        PredefinedFilters.Insert(0, RgfFilterSettings.DeepCopy(filter));
        return true;
    }

    public async Task<bool> DeleteFilterSettingsAsync(int filterSettingsId)
    {
        var success = await _manager.DeleteFilterSettingsAsync(filterSettingsId);
        if (!success)
        {
            return false;
        }
        //We need a new list for the ComboBox DataSource to refresh.
        PredefinedFilters = PredefinedFilters.Where(e => e.FilterSettingsId != filterSettingsId).ToList();
        return true;
    }
    #endregion
}

public class RgfFilterProperty : IRgfProperty
{
    public RgfFilterProperty(RgfProperty property, RgfFilter.Column filter)
    {
        _property = property;
        _filter = filter;
    }

    private readonly RgfProperty _property;
    private readonly RgfFilter.Column _filter;

    public RgfFilter.QueryOperator[] Operators => _filter.Operators;

    public List<RgfFilter.DictionaryItem> DictionaryItems => _filter.Dictionary;

    public Dictionary<string, string> Dictionary => _filter.Dictionary.ToDictionary(e => e.Key, e => e.Value);

    #region IRgfProperty
    public int Id { get => _property.Id; set { _property.Id = value; } }

    public bool IsKey { get => _property.IsKey; set { _property.IsKey = value; } }

    public string Alias { get => _property.Alias; set { _property.Alias = value; } }

    public string ClientName { get => _property.ClientName; set { _property.ClientName = value; } }

    public string BaseEntityNameVersion { get => _property.BaseEntityNameVersion; set { _property.BaseEntityNameVersion = value; } }

    public int ColPos { get => _property.ColPos; set { _property.ColPos = value; } }

    public string ColTitle { get => _property.ColTitle; set { _property.ColTitle = value; } }

    public int ColWidth { get => _property.ColWidth; set { _property.ColWidth = value; } }

    public bool Editable { get => _property.Editable; set { _property.Editable = value; } }

    public string Ex { get => _property.Ex; set { _property.Ex = value; } }

    public PropertyListType ListType { get => _property.ListType; set { _property.ListType = value; } }

    public PropertyFormType FormType { get => _property.FormType; set { _property.FormType = value; } }

    public ClientDataType ClientDataType => _property.ClientDataType;

    public Dictionary<string, object> Options { get => _property.Options; set { _property.Options = value; } }

    public bool Orderable { get => _property.Orderable; set { _property.Orderable = value; } }

    public bool Readable { get => _property.Readable; set { _property.Readable = value; } }

    public int Sort { get => _property.Sort; set { _property.Sort = value; } }
    #endregion
}
