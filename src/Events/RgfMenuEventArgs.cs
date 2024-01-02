using Recrovit.RecroGridFramework.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfMenuEventArgs : EventArgs
{
    public RgfMenuEventArgs(string command, RgfDynamicDictionary? data = null)
    {
        Command = command;
        Data = data ?? new();
    }

    public string Command { get; }
    public RgfDynamicDictionary Data { get; }
}
