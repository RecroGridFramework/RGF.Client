using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recrovit.RecroGridFramework.Client.Mappings;

public static class Toolbar
{
    public static ToolbarAction MenuCommand2ToolbarAction(string menuCommand)
    {
        if (menuCommand == Menu.ColumnSettings) return ToolbarAction.ColumnSettings;
        if (menuCommand == Menu.SaveSettings) return ToolbarAction.SaveSettings;
        if (menuCommand == Menu.ResetSettings) return ToolbarAction.ResetSettings;

        if (menuCommand == Menu.RecroTrack) return ToolbarAction.RecroTrack;
        if (menuCommand == Menu.QueryString) return ToolbarAction.QueryString;
        if (menuCommand == Menu.QuickWatch) return ToolbarAction.QuickWatch;

        if (menuCommand == Menu.RgfAbout) return ToolbarAction.RgfAbout;

        return ToolbarAction.Invalid;
    }
}
