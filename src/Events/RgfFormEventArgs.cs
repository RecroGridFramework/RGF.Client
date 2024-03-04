using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfFormEventKind
{
    FormDataInitialized,
    ValidationRequested,
}

public class RgfFormEventArgs : EventArgs
{
    public RgfFormEventArgs(RgfFormEventKind eventKind, ComponentBase formComponent, FieldIdentifier? fieldId = null, RgfForm.Property? property = null)
    {
        EventKind = eventKind;
        BaseFormComponent = formComponent;
        FieldId = fieldId;
        Property = property;
    }

    public RgfFormEventKind EventKind { get; }

    public ComponentBase BaseFormComponent { get; }

    public FieldIdentifier? FieldId { get; internal set; }

    public RgfForm.Property? Property { get; internal set; }
}