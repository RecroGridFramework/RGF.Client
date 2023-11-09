using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    RecroTrack,
    QueryString,
    QuickWatch,

    RgfAbout,
}

public class RgfToolbarEventArgs : EventArgs
{
    public RgfToolbarEventArgs(ToolbarAction command)
    {
        Command = command;
    }

    public ToolbarAction Command { get; }
}
