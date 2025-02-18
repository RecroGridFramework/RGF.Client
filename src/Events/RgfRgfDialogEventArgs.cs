namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfDialogEventKind
{
    Initialized = 1,
    Close = 2,
    Destroy = 3,
    Refresh = 4,
}

public class RgfDialogEventArgs : EventArgs
{
    public RgfDialogEventArgs(RgfDialogEventKind eventKind)
    {
        EventKind = eventKind;
    }

    public RgfDialogEventKind EventKind { get; }
}