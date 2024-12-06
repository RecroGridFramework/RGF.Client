using Recrovit.RecroGridFramework.Client.Handlers;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfEntityEventKind
{
    Initialized = 1,
    Destroy = 2
}

public class RgfEntityEventArgs : EventArgs
{
    public RgfEntityEventArgs(RgfEntityEventKind eventKind, IRgManager manager)
    {
        EventKind = eventKind;
        Manager = manager;
    }

    public RgfEntityEventKind EventKind { get; }

    public IRgManager Manager { get; }
}