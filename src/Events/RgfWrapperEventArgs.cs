using Microsoft.AspNetCore.Components;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfWrapperEventKind
{
    Rendered = 1
}

public class RgfWrapperEventArgs<TWrapper> : EventArgs where TWrapper : ComponentBase
{
    public RgfWrapperEventArgs(RgfWrapperEventKind eventKind, TWrapper wrapperComponent)
    {
        EventKind = eventKind;
        WrapperComponent = wrapperComponent;
    }

    public static RgfWrapperEventArgs<T> CreateAfterRenderEvent<T>(T wrapperComponent, bool firstRender) where T : ComponentBase
        => new RgfWrapperEventArgs<T>(RgfWrapperEventKind.Rendered, wrapperComponent) { FirstRender = firstRender };

    public RgfWrapperEventKind EventKind { get; }

    public TWrapper WrapperComponent { get; }

    public bool FirstRender { get; internal set; }
}