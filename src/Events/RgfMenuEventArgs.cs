using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfMenuEventArgs : EventArgs
{
    public RgfMenuEventArgs(string command, RgfMenuType menuType = RgfMenuType.Invalid, RgfEntityKey? entityKey = null, RgfDynamicDictionary? data = null)
    {
        Command = command;
        MenuType = menuType;
        EntityKey = entityKey;
        Data = data;
    }

    public string Command { get; }

    public RgfMenuType MenuType { get; }

    public RgfEntityKey? EntityKey { get;}

    public RgfDynamicDictionary? Data { get;}
}