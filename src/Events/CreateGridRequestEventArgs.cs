using Recrovit.RecroGridFramework.Abstraction.Contracts.API;

namespace Recrovit.RecroGridFramework.Client.Events;

public class CreateGridRequestEventArgs : EventArgs
{
    public RgfGridRequest Request { get; set; }

    public CreateGridRequestEventArgs(RgfGridRequest request)
    {
        Request = request;
    }
}