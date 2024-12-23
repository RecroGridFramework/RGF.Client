using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfToolbarEventKind
{
    Invalid = 0,
    Refresh = 1,
    ShowFilter = 2,
    Add = 3,
    Edit = 4,
    Read = 5,
    Delete = 6,
    Select = 7,
    RecroChart = 8,
    ToggleDisplayMode = 9,
    ToggleQuickFilter = 10,
}

public class RgfToolbarEventArgs : EventArgs
{
    public RgfToolbarEventArgs(RgfToolbarEventKind eventKind, RgfDynamicDictionary? data = null)
    {
        EventKind = eventKind;
        Data = data;
    }

    public RgfToolbarEventKind EventKind { get; }

    public RgfDynamicDictionary? Data { get; }
}