using Microsoft.AspNetCore.Components;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfListEventKind
{
    CreateRowData = 1,
    ColumnSettingsChanged = 2,
    AfterRender = 3
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

    public static RgfListEventArgs CreateAfterRenderEvent(ComponentBase gridComponent, bool firstRender) => new RgfListEventArgs(RgfListEventKind.AfterRender, gridComponent) { FirstRender = firstRender };

    public RgfListEventKind EventKind { get; }

    public ComponentBase? BaseGridComponent { get; }

    public RgfDynamicDictionary? Data { get; }

    public IEnumerable<RgfProperty>? Properties { get; }

    public bool FirstRender { get; internal set; }
}