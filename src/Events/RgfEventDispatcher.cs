using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Events;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfEventDispatcher<TEnum, TArgs> where TEnum : Enum where TArgs : EventArgs
{
    private Dictionary<TEnum, EventDispatcher<IRgfEventArgs<TArgs>>> _eventHandlers = new();

    public void Subscribe(TEnum eventName, Func<IRgfEventArgs<TArgs>, Task> handler)
    {
        if (handler != null )
        {
            EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
            if (!_eventHandlers.TryGetValue(eventName, out handlers))
            {
                handlers = new();
                _eventHandlers.Add(eventName, handlers);
            }
            handlers.Subscribe(handler);
        }
    }

    public void Subscribe(TEnum eventName, Action<IRgfEventArgs<TArgs>> handler)
    {
        if (handler != null)
        {
            EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
            if (!_eventHandlers.TryGetValue(eventName, out handlers))
            {
                handlers = new();
                _eventHandlers.Add(eventName, handlers);
            }
            handlers.Subscribe(handler);
        }
    }

    public void Unsubscribe(TEnum eventName, Func<IRgfEventArgs<TArgs>, Task> handler)
    {
        if (handler != null && _eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Unsubscribe(handler);
        }
    }

    public void Unsubscribe(TEnum eventName, Action<IRgfEventArgs<TArgs>> handler)
    {
        if (handler != null && _eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Unsubscribe(handler);
        }
    }

    public Task DispatchEventAsync(TEnum eventName, IRgfEventArgs<TArgs> args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            return handlers.InvokeAsync(args);
        }
        return Task.CompletedTask;
    }
}

public class RgfEventArgs<TArgs> : IRgfEventArgs<TArgs> where TArgs : EventArgs
{
    public RgfEventArgs(object sender, TArgs args)
    {
        Sender = sender;
        Args = args;
    }
    public object Sender { get; }

    public TArgs Args { get; }
}
