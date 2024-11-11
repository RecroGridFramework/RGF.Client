using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfMenuEventArgs : EventArgs
{
    public RgfMenuEventArgs(string command, string title, RgfMenuType menuType = RgfMenuType.Invalid, RgfEntityKey? entityKey = null, RgfDynamicDictionary? data = null)
    {
        Command = command;
        Title = title;
        MenuType = menuType;
        EntityKey = entityKey;
        Data = data;
    }

    public string Title { get; }

    public string Command { get; }

    public RgfMenuType MenuType { get; }

    public RgfEntityKey? EntityKey { get;}

    public RgfDynamicDictionary? Data { get;}
}