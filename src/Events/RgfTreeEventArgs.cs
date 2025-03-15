using Microsoft.AspNetCore.Components;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfTreeEventKind
{
    NodeParametersSet = 1
}

public class RgfTreeEventArgs : EventArgs
{
    public RgfTreeEventArgs(RgfTreeEventKind eventKind, ComponentBase treeComponent, IRgfTreeNodeParameters rgfTreeNodeParameters)
    {
        EventKind = eventKind;
        TreeComponentBase = treeComponent;
        RgfTreeNodeParameters = rgfTreeNodeParameters;
    }

    public RgfTreeEventKind EventKind { get; }

    public ComponentBase TreeComponentBase { get; }

    public IRgfTreeNodeParameters RgfTreeNodeParameters { get; }
}

public interface IRgfTreeNodeParameters
{
    int AbsoluteRowIndex { get; set; }

    RenderFragment? EmbeddedGrid { get; set; }
 
    bool IsExpanded { get; set; }

    RgfProperty? Property { get; set; }

    RgfDynamicDictionary RowData { get; set; }

    string? TooltipText { get; set; }
}