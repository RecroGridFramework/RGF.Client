using Recrovit.RecroGridFramework.Abstraction.Contracts.API;
using Recrovit.RecroGridFramework.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum ListViewAction
{
    RefreshRow,
    AddRow,
    DeleteRow,
}

public class RgfListViewEventArgs : EventArgs
{
    public RgfListViewEventArgs(ListViewAction command, RgfDynamicDictionary data)
    {
        Command = command;
        Data = data;
    }

    public ListViewAction Command { get; }
    public RgfDynamicDictionary Data { get; set; }

    public static bool Create(ListViewAction command, RgfGridResult data, out RgfListViewEventArgs? rowData) => Create(command, data.DataColumns, data.Data[0], out rowData);
    public static bool Create(ListViewAction command, string[]? dataColumns, object[]? data, out RgfListViewEventArgs? rowData)
    {
        if (dataColumns != null && data != null)
        {
            rowData = new(command, new RgfDynamicDictionary(dataColumns, data));
            return true;
        }
        rowData = null;
        return false;
    }
}
