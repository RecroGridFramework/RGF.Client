using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfEventDispatcher<TEnum, TArgs> where TEnum : Enum where TArgs : EventArgs
{
    private Dictionary<TEnum, EventHandler<IRgfEventArgs<TArgs>>> _eventHandlers = new();

    public void Subscribe(TEnum eventName, EventHandler<IRgfEventArgs<TArgs>> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] += handler;
        }
        else
        {
            _eventHandlers[eventName] = handler;
        }
    }

    public void Unsubscribe(TEnum eventName, EventHandler<IRgfEventArgs<TArgs>> handler)
    {
        if (handler != null && _eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers -= handler;
        }
    }

    public void DispatchEvent(TEnum eventName, IRgfEventArgs<TArgs> args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Invoke(args.Sender, args);
        }
    }
}

public class RgfEventArgs<TArgs> : IRgfEventArgs<TArgs> where TArgs : EventArgs
{
    public RgfEventArgs(object sender, TArgs args)
    {
        Args = args;
        Sender = sender;
    }

    public TArgs Args { get; set; }
    public object Sender { get; set; }
}
