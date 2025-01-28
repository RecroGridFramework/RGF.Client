using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Extensions;
using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Models;
using System.Data;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgFormHandler : IDisposable
{
    Task<RgfResult<RgfFormResult>> InitializeAsync(RgfEntityKey data);

    bool InitFormData(RgfFormResult formResult, out FormViewData? formViewData);

    RgfDynamicDictionary CollectChangedFormData(FormViewData formViewData);

    Task<RgfResult<RgfFormResult>> SaveAsync(FormViewData formViewData, bool refresh = false);

    bool IsModified(FormViewData formViewData, RgfForm.Property property);
}

internal class RgFormHandler : IRgFormHandler
{
    public RgFormHandler(ILogger<RgFormHandler> logger, IRgManager manager)
    {
        _logger = logger;
        _manager = manager;
        _recroDict = manager.ServiceProvider.GetRequiredService<IRecroDictService>();
    }

    public static IRgFormHandler Create(IRgManager manager)
    {
        var logger = manager.ServiceProvider.GetRequiredService<ILogger<RgFormHandler>>();
        var handler = new RgFormHandler(logger, manager);
        return handler;
    }

    public async Task<RgfResult<RgfFormResult>> InitializeAsync(RgfEntityKey data)
    {
        var param = _manager.CreateGridRequest();
        if (data?.Keys?.Any() == true)
        {
            param.EntityKey = data;
        }
        var result = await _manager.GetFormAsync(param);
        if (result.Success || result.Messages != null)
        {
            await _manager.BroadcastMessages(result.Messages, this);
        }
        return result;
    }

    private readonly ILogger _logger;

    private readonly IRgManager _manager;

    private readonly IRecroDictService _recroDict;

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
            var flexWidthTab = _manager.EntityDesc.Options?.GetIntValue($"RGO_FormFlexColumnWidth-{tab.Index}", defaultValue: int.MaxValue) ?? int.MaxValue;
            foreach (var group in tab.Groups)
            {
                var flexWidthGroup = _manager.EntityDesc.Options?.GetIntValue($"RGO_FormFlexColumnWidth-{tab.Index}-{group.Index}", defaultValue: int.MaxValue) ?? int.MaxValue;
                if (flexWidthGroup != int.MaxValue)
                {
                    group.FlexColumnWidth = flexWidthGroup;
                }
                else
                {
                    group.FlexColumnWidth = flexWidthTab != int.MaxValue ? flexWidthTab : flexWidthEntity;
                    if (group.Properties.Count == 1)
                    {
                        var prop = group.Properties.Single();
                        prop.EntityDesc = _manager.EntityDesc;
                        if (prop.PropertyDesc == null)
                        {
                            prop.PropertyDesc = prop.EntityDesc.Properties.SingleOrDefault(e => e.Id == prop.Id);
                        }
                        if ((prop.PropertyDesc?.Options?.GetIntValue("RGO_FormFlexColumnWidth", defaultValue: int.MaxValue) ?? int.MaxValue) == int.MaxValue)
                        {
                            switch (prop.PropertyDesc?.FormType)
                            {
                                case PropertyFormType.TextBoxMultiLine:
                                case PropertyFormType.HtmlEditor:
                                case PropertyFormType.RecroGrid:
                                    group.FlexColumnWidth = 12;
                                    break;
                            }
                        }
                    }
                }
            }
        }
        foreach (var prop in form.FormTabs.SelectMany(e => e.Groups.SelectMany(g => g.Properties)))
        {
            prop.EntityDesc = _manager.EntityDesc;
            prop.PropertyDesc ??= prop.EntityDesc.Properties.SingleOrDefault(e => e.Id == prop.Id);
            var flexWidth = prop.PropertyDesc?.Options?.GetIntValue("RGO_FormFlexColumnWidth", defaultValue: int.MaxValue) ?? int.MaxValue;
            prop.FlexColumnWidth = flexWidth != int.MaxValue ? (int)flexWidth : null;

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

        var dataRec = RgfDynamicDictionary.Create(_manager.ServiceProvider.GetRequiredService<ILogger<RgfDynamicDictionary>>(), _manager.EntityDesc, data);
        formViewData = new FormViewData(form.FormTabs, dataRec)
        {
            EntityKey = formResult?.EntityKey,
            //IsNewEntry = isNewEntry,
            StyleSheetUrl = formResult?.StyleSheetUrl,
        };
        return true;
    }

    public async Task<RgfResult<RgfFormResult>> SaveAsync(FormViewData formViewData, bool refresh = false)
    {
        _logger.LogDebug("SaveAsync");
        var param = _manager.CreateGridRequest((request) =>
        {
            request.EntityKey = formViewData.EntityKey;
            if (refresh)
            {
                request.Skeleton = true;
            }
        });
        bool isNewRow = param.EntityKey?.Keys.Any() != true;
        param.Data = CollectChangedFormData(formViewData);
        if (!param.Data.Any())
        {
            return new RgfResult<RgfFormResult>() { Success = !isNewRow };
        }

        var toast = RgfToastEventArgs.CreateActionEvent(_recroDict.GetRgfUiString("Request"), _manager.EntityDesc.Title, _recroDict.GetRgfUiString("Save"));
        await _manager.ToastManager.RaiseEventAsync(toast, this);
        var res = await _manager.UpdateFormDataAsync(param);
        if (res.Success && res.Result?.GridResult != null)
        {
            await _manager.ToastManager.RaiseEventAsync(toast.RecreateAsSuccess(_recroDict.GetRgfUiString("Processed")), this);
            if (isNewRow)
            {
                await _manager.ListHandler.AddRowAsync(new RgfDynamicDictionary(res.Result.GridResult.DataColumns, res.Result.GridResult.Data[0]));
            }
            else
            {
                await _manager.ListHandler.RefreshRowAsync(new RgfDynamicDictionary(res.Result.GridResult.DataColumns, res.Result.GridResult.Data[0]));
            }
        }
        return res;
    }

    public RgfDynamicDictionary CollectChangedFormData(FormViewData formViewData)
    {
        var changes = new RgfDynamicDictionary();
        var origProps = formViewData.FormTabs.SelectMany(tab => tab.Groups.SelectMany(g => g.Properties)).ToArray();
        foreach (var name in formViewData.DataRec.GetDynamicMemberNames())
        {
            var prop = _manager.EntityDesc.Properties.SingleOrDefault(e => e.Alias.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (prop != null && prop.FormType != PropertyFormType.RecroGrid && prop.FormType != PropertyFormType.ImageInDB)
            {
                var newData = formViewData.DataRec.GetItemData(name);
                var orig = origProps.SingleOrDefault(e => e.Id == prop.Id);
                _logger.LogTrace("ChangedFormData.Chk: name:{name}={new}", name, newData);
                if (orig != null)
                {
                    if (orig.ForeignEntity?.EntityKeys.Any() == true)
                    {
                        var ek = orig.ForeignEntity?.EntityKeys.First();
                        var fkProp = _manager.EntityDesc.Properties.SingleOrDefault(e => e.Id == ek!.Foreign);
                        if (fkProp != null && formViewData.DataRec.GetItemData(fkProp.Alias).Value != null)
                        {
                            //The key is also provided, so we omit the filter string.
                            _logger.LogDebug("ChangedFormData.Skip: name:{name}", name);
                            continue;
                        }
                    }
                    var origData = new RgfDynamicData(prop.ClientDataType, orig.OrigValue);
                    if (!origData.Equals(newData))
                    {
                        _logger.LogDebug("ChangedFormData: name:{name}, new:{new}, orig:{orig}, origstr:{origstr}", name, newData, origData, orig.OrigValue);
                        changes.SetMember(prop.ClientName, newData.ToString());
                    }
                }
                else if (newData?.Value != null)
                {
                    changes.SetMember(prop.ClientName, newData.ToString());
                }
            }
        }
        return changes;
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