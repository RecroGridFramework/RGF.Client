using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum UserMessageType
{
    None = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

public enum UserMessageOrigin
{
    Global = 1,
    FormView = 2,
}

public class RgfUserMessageEventArgs : EventArgs
{
    public RgfUserMessageEventArgs(UserMessageType category, string message, string title, UserMessageOrigin origin = UserMessageOrigin.Global)
    {
        Category = category;
        Message = message;
        Title = title;
        Origin = origin;
    }

    public RgfUserMessageEventArgs(IRecroDictService recroDict, UserMessageType category, string message, UserMessageOrigin origin = UserMessageOrigin.Global) : this(category, message, category.ToString(), origin)
    {
        Title = recroDict.GetRgfUiString(category.ToString());
    }

    public UserMessageType Category { get; set; }

    public UserMessageOrigin Origin { get; set; }

    public string Message { get; set; }

    public string Title { get; set; }
}
