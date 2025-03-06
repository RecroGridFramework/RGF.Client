namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfDialogEventKind
{
    Initialized = 1,
    Close = 2,
    Destroy = 3,
    Refresh = 4,
    Rendered = 5
}

public class RgfDialogEventArgs : EventArgs
{
    public RgfDialogEventArgs(RgfDialogEventKind eventKind)
    {
        EventKind = eventKind;
    }

    public static RgfDialogEventArgs CreateAfterRenderEvent(bool firstRender) => new RgfDialogEventArgs(RgfDialogEventKind.Rendered) { FirstRender = firstRender };

    public RgfDialogEventKind EventKind { get; }

    public bool FirstRender { get; internal set; }
}