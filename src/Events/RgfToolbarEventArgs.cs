using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum ToolbarAction
{
    Invalid,

    Refresh,
    ShowFilter,
    Add,
    Edit,
    Read,
    Delete,
    Select,

    ColumnSettings,
    SaveSettings,
    ResetSettings,

    EntityEditor,
    RecroTrack,
    QueryString,
    QuickWatch,
    ExportCsv,

    RgfAbout
}

public class RgfToolbarEventArgs : EventArgs
{
    public RgfToolbarEventArgs(ToolbarAction command, RgfDynamicDictionary? data = null)
    {
        Command = command;
        Data = data;
    }

    public ToolbarAction Command { get; }

    public RgfDynamicDictionary? Data { get; }
}