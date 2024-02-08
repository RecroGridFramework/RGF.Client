using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Models;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgFormHandler : IDisposable
{
    Task<RgfResult<RgfFormResult>> InitializeAsync(RgfEntityKey data);
    bool InitFormData(RgfFormResult formResult, out FormViewData? formViewData);
    Task<RgfResult<RgfFormResult>> SaveAsync(FormViewData formViewData, bool skeleton = false);
    bool IsModified(FormViewData formViewData, RgfForm.Property property);
}

internal class RgFormHandler : IRgFormHandler
{
    public RgFormHandler(ILogger<RgFormHandler> logger, IRgManager manager)
    {
        _manager = manager;
        _logger = logger;
    }

    public static IRgFormHandler Create(IRgManager manager)
    {
        var logger = manager.ServiceProvider.GetRequiredService<ILogger<RgFormHandler>>();
        var handler = new RgFormHandler(logger, manager);
        return handler;
    }

    public async Task<RgfResult<RgfFormResult>> InitializeAsync(RgfEntityKey data)
    {
        var param = new RgfGridRequest(_manager.SessionParams);
        if (data?.Keys?.Any() == true)
        {
            param.EntityKey = data;
        }
        var result = await _manager.GetFormAsync(param);
        if (result.Success || result.Messages != null)
        {
            _manager.BroadcastMessages(result.Messages, this);
        }
        return result;
    }

    private ILogger _logger { get; set; }

    private IRgManager _manager { get; }


    public bool InitFormData(RgfFormResult formResult, out FormViewData? formViewData)
    {
        RgfForm form;
        if (!RgfForm.Deserialize(formResult?.XmlForm, out form))
        {
            formViewData = null;
            return false;
        }

        bool isNewEntry = formResult?.EntityKey?.Keys.Any() != true;
        var flexWidthEntity = _manager.EntityDesc.Options?.GetIntValue("RGO_FormFlexColumnWidth", 6) ?? 6;
        Dictionary<string, object?> data = new();
        foreach (var tab in form.FormTabs)
        {
            var flexWidthTab = _manager.EntityDesc.Options?.GetIntValue($"RGO_FormFlexColumnWidth-{tab.Index}", 0) ?? 0;
            foreach (var group in tab.Groups)
            {
                var flexWidthGroup = _manager.EntityDesc.Options?.GetIntValue($"RGO_FormFlexColumnWidth-{tab.Index}-{group.Index}", 0) ?? 0;
                if (flexWidthGroup > 0)
                {
                    group.FlexColumnWidth = flexWidthGroup;
                }
                else
                {
                    group.FlexColumnWidth = flexWidthTab > 0 ? flexWidthTab : flexWidthEntity;
                    if (group.Properties.Count == 1)
                    {
                        var prop = group.Properties.Single();
                        prop.EntityDesc = _manager.EntityDesc;
                        if (prop.PropertyDesc == null)
                        {
                            prop.PropertyDesc = prop.EntityDesc.Properties.SingleOrDefault(e => e.Id == prop.Id);
                        }
                        switch (prop.PropertyDesc?.FormType)
                        {
                            case PropertyFormType.TextBox:
                            case PropertyFormType.CheckBox:
                            case PropertyFormType.DropDown:
                            case PropertyFormType.Date:
                            case PropertyFormType.DateTime:
                                break;

                            default:
                                group.FlexColumnWidth = 12;
                                break;
                        }
                    }
                }
            }
        }
        foreach (var prop in form.FormTabs.SelectMany(e => e.Groups.SelectMany(g => g.Properties)))
        {
            prop.EntityDesc = _manager.EntityDesc;
            prop.PropertyDesc ??= prop.EntityDesc.Properties.SingleOrDefault(e => e.Id == prop.Id);
            var flexWidth = prop.PropertyDesc?.Options?.GetIntValue("RGO_FormFlexColumnWidth", 0) ?? 0;
            prop.FlexColumnWidth = flexWidth > 0 ? (int)flexWidth : null;

            if (prop.Alias != null)
            {
                data[prop.Alias] = prop.OrigValue;
                _logger.LogDebug("RgfForm.Deserialize => Id:{Id}, Alias={Alias}, Value:{Value}", prop.Id, prop.Alias, prop.OrigValue);
                if (isNewEntry)
                {
                    if (prop.OrigValue != null && prop.AvailableItems?.Any(e => e.Key == prop.OrigValue) != true)
                    {
                        prop.OrigValue = null;
                    }
                }
                else if (prop.ForeignEntity != null)
                {
                    //TODO: multiple foreignKey
                    var key = prop.ForeignEntity.EntityKeys.FirstOrDefault();
                    if (key != null && !_manager.EntityDesc.Properties.Any(e => e.Id == key.Key && e.IsKey))
                    {
                        data[$"rg-col-{key.Key}"] = key.Value;
                    }
                }
            }
            else
            {
                _logger.LogWarning("RgfForm.Deserialize => Alias=NULL => Entity:{Entity}, Id:{Id}", prop.EntityDesc.NameVersion, prop.Id);
            }
        }

        var checkBoxes = _manager.EntityDesc.Properties.Where(e => e.FormType == PropertyFormType.CheckBox && e.Editable && e.Required);
        foreach (var prop in checkBoxes)
        {
            if (!data.TryGetValue(prop.Alias, out object? value) || value == null)
            {
                data[prop.Alias] = bool.FalseString;
            }
        }

        var dataRec = RgfDynamicDictionary.Create(_logger, _manager.EntityDesc, data);
        formViewData = new FormViewData(form.FormTabs, dataRec)
        {
            EntityKey = formResult?.EntityKey,
            //IsNewEntry = isNewEntry,
            StyleSheetUrl = formResult?.StyleSheetUrl,
        };
        return true;
    }

    public async Task<RgfResult<RgfFormResult>> SaveAsync(FormViewData formViewData, bool skeleton = false)
    {
        _logger.LogDebug("SaveAsync");
        RgfGridRequest param = new(_manager.SessionParams);
        param.EntityKey = formViewData.EntityKey;
        if (skeleton)
        {
            param.Skeleton = true;
        }
        param.Data = new();

        bool isNewRow = param.EntityKey?.Keys.Any() != true;

        var origProps = formViewData.FormTabs.SelectMany(tab => tab.Groups.SelectMany(g => g.Properties)).ToArray();
        foreach (var name in formViewData.DataRec.GetDynamicMemberNames())
        {
            var prop = _manager.EntityDesc.Properties.SingleOrDefault(e => e.Alias.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (prop != null && prop.FormType != PropertyFormType.RecroGrid && prop.FormType != PropertyFormType.ImageInDB)
            {
                var newData = formViewData.DataRec.GetItemData(name);
                var orig = origProps.SingleOrDefault(e => e.Id == prop.Id);
                _logger.LogDebug("SaveData: name:{name}={new}", name, newData);
                if (orig != null)
                {
                    if (orig.ForeignEntity?.EntityKeys.Any() == true)
                    {
                        var ek = orig.ForeignEntity?.EntityKeys.First();
                        var fkProp = _manager.EntityDesc.Properties.SingleOrDefault(e => e.Id == ek!.Foreign);
                        if (fkProp != null && formViewData.DataRec.GetItemData(fkProp.Alias).Value != null)
                        {
                            //The key is also provided, so we omit the filter string.
                            _logger.LogDebug("SaveData.Skip: name:{name}", name);
                            continue;
                        }
                    }
                    var origData = new RgfDynamicData(prop.ClientDataType, orig.OrigValue);
                    if (!origData.Equals(newData))
                    {
                        _logger.LogDebug("SaveData.ChangeData: name:{name}, new:{new}, orig:{orig}", name, newData, origData);
                        param.Data.SetMember(prop.ClientName, newData.ToString());
                    }
                }
                else if (newData?.Value != null)
                {
                    param.Data.SetMember(prop.ClientName, newData.ToString());
                }
            }
        }
        if (!param.Data.Any())
        {
            return new RgfResult<RgfFormResult>() { Success = !isNewRow };
        }
        var res = await _manager.UpdateFormDataAsync(param);
        if (res.Success && res.Result?.GridResult != null
            && RgfListViewEventArgs.Create(isNewRow ? ListViewAction.AddRow : ListViewAction.RefreshRow, res.Result.GridResult, out var arg))
        {
            _manager.NotificationManager.RaiseEvent(arg, this);
        }
        return res;
    }

    public bool IsModified(FormViewData formViewData, RgfForm.Property property)
    {
        var orig = formViewData.FormTabs.SelectMany(tab => tab.Groups.SelectMany(g => g.Properties)).SingleOrDefault(e => e.Id == property.Id);
        if (orig != null)
        {
            var newData = formViewData.DataRec.GetItemData(property.Alias);
            var origData = new RgfDynamicData(property.PropertyDesc.ClientDataType, orig.OrigValue);
            return !origData.Equals(newData);
        }
        return true;
    }

    public void Dispose() { }
}
