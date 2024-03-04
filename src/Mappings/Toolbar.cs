using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Client.Events;

namespace Recrovit.RecroGridFramework.Client.Mappings;

public static class Toolbar
{
    public static RgfToolbarEventKind MenuCommand2ToolbarAction(string menuCommand)
    {
        if (menuCommand == Menu.ColumnSettings) return RgfToolbarEventKind.ColumnSettings;
        if (menuCommand == Menu.SaveSettings) return RgfToolbarEventKind.SaveSettings;
        if (menuCommand == Menu.ResetSettings) return RgfToolbarEventKind.ResetSettings;

        if (menuCommand == Menu.RecroTrack) return RgfToolbarEventKind.RecroTrack;
        if (menuCommand == Menu.QueryString) return RgfToolbarEventKind.QueryString;
        if (menuCommand == Menu.QuickWatch) return RgfToolbarEventKind.QuickWatch;

        if (menuCommand == Menu.ExportCsv) return RgfToolbarEventKind.ExportCsv;

        if (menuCommand == Menu.RgfAbout) return RgfToolbarEventKind.RgfAbout;

        if (menuCommand == Menu.EntityEditor) return RgfToolbarEventKind.EntityEditor;

        return RgfToolbarEventKind.Invalid;
    }
}