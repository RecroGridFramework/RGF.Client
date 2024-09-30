using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfFormEventKind
{
    Invalid = 0,
    FormDataInitialized,
    AfterRender,
    ValidationRequested,
    FindEntity,
    ParametersSet
}

public class RgfFormEventArgs : EventArgs
{
    public RgfFormEventArgs(RgfFormEventKind eventKind, ComponentBase formComponent, FieldIdentifier? fieldId = null, RgfForm.Property? property = null, RgfSelectParam? selectParam = null)
    {
        EventKind = eventKind;
        BaseFormComponent = formComponent;
        FieldId = fieldId;
        Property = property;
        SelectParam = selectParam;
    }

    public static RgfFormEventArgs CreateAfterRenderEvent(ComponentBase formComponent, bool firstRender) => new RgfFormEventArgs(RgfFormEventKind.AfterRender, formComponent) { FirstRender = firstRender };

    public RgfFormEventKind EventKind { get; }

    public ComponentBase BaseFormComponent { get; }

    public FieldIdentifier? FieldId { get; internal set; }

    public RgfForm.Property? Property { get; internal set; }

    public RgfSelectParam? SelectParam { get; internal set; }

    public bool FirstRender { get; internal set; }
}