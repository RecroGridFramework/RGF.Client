using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfFormEventKind
{
    FormDataInitialized = 1,
    Rendered = 2,
    ValidationRequested = 3,
    EntitySearch = 4,
    EntityDisplay = 5,
    ParametersSet = 6,//ParametersApplied
    FormSaveStarted = 7,
    FormItemsFirstRenderCompleted = 8
}

public class RgfFormEventArgs : EventArgs
{
    public RgfFormEventArgs(RgfFormEventKind eventKind, ComponentBase formComponent, FieldIdentifier? fieldId = null, RgfForm.Property? property = null, RgfSelectParam? selectParam = null, bool close = false)
    {
        EventKind = eventKind;
        BaseFormComponent = formComponent;
        FieldId = fieldId;
        Property = property;
        SelectParam = selectParam;
        Close = close;
    }

    public static RgfFormEventArgs CreateAfterRenderEvent(ComponentBase formComponent, bool firstRender) => new RgfFormEventArgs(RgfFormEventKind.Rendered, formComponent) { FirstRender = firstRender };

    public RgfFormEventKind EventKind { get; }

    public ComponentBase BaseFormComponent { get; }

    public FieldIdentifier? FieldId { get; internal set; }

    public RgfForm.Property? Property { get; internal set; }

    public RgfSelectParam? SelectParam { get; internal set; }

    public bool FirstRender { get; internal set; }

    public bool Close { get; internal set; }
}