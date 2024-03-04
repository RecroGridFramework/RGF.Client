using Microsoft.AspNetCore.Components;
using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfListEventKind
{
    RefreshRow,
    AddRow,
    DeleteRow,

    CreateRowData,
    ColumnSettingsChanged
}

public class RgfListEventArgs : EventArgs
{
    public RgfListEventArgs(RgfListEventKind eventKind, ComponentBase? gridComponent, RgfDynamicDictionary? data = null, IEnumerable<RgfProperty>? properties = null)
    {
        EventKind = eventKind;
        BaseGridComponent = gridComponent;
        Data = data;
        Properties = properties;
    }

    public RgfListEventKind EventKind { get; }

    public ComponentBase? BaseGridComponent { get; }

    public RgfDynamicDictionary? Data { get; }

    public IEnumerable<RgfProperty>? Properties { get; }

    public static bool Create(RgfListEventKind eventKind, RgfGridResult data, out RgfListEventArgs? rowData) => Create(eventKind, data.DataColumns, data.Data[0], out rowData);

    public static bool Create(RgfListEventKind eventKind, string[]? dataColumns, object[]? data, out RgfListEventArgs? rowData)
    {
        if (dataColumns != null && data != null)
        {
            rowData = new(eventKind, null, new RgfDynamicDictionary(dataColumns, data));
            return true;
        }
        rowData = null;
        return false;
    }
}