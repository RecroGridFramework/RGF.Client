using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Events;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfEventDispatcher<TEnum, TArgs> where TEnum : Enum where TArgs : EventArgs
{
    private Dictionary<TEnum, EventDispatcher<IRgfEventArgs<TArgs>>> _eventHandlers = [];

    private Dictionary<TEnum, EventDispatcher<IRgfEventArgs<TArgs>>> _defaultHandlers = [];

    public void Subscribe(TEnum eventName, Func<IRgfEventArgs<TArgs>, Task> handler, bool callHandlerIfUnhandled = false)
    {
        if (handler != null)
        {
            EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
            if (callHandlerIfUnhandled)
            {
                if (!_defaultHandlers.TryGetValue(eventName, out handlers))
                {
                    handlers = new();
                    _defaultHandlers.Add(eventName, handlers);
                }
            }
            else
            {
                if (!_eventHandlers.TryGetValue(eventName, out handlers))
                {
                    handlers = new();
                    _eventHandlers.Add(eventName, handlers);
                }
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

    public async Task<bool> DispatchEventAsync(TEnum eventName, IRgfEventArgs<TArgs> args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            await handlers.InvokeAsync(args);
        }
        if (!args.Handled)
        {
            if (_defaultHandlers.TryGetValue(eventName, out handlers))
            {
                await handlers.InvokeAsync(args);
            }
        }
        return args.Handled;
    }

    public void Subscribe(TEnum[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler, bool callHandlerIfUnhandled = false) => Array.ForEach(eventNames, (e) => Subscribe(e, handler, callHandlerIfUnhandled));
    public void Subscribe(TEnum[] eventNames, Action<IRgfEventArgs<TArgs>> handler, bool callHandlerIfUnhandled = false) => Array.ForEach(eventNames, (e) => Subscribe(e, handler));
    public void Unsubscribe(TEnum[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));
    public void Unsubscribe(TEnum[] eventNames, Action<IRgfEventArgs<TArgs>> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));
}

public class RgfEventArgs<TArgs> : IRgfEventArgs<TArgs> where TArgs : EventArgs
{
    public RgfEventArgs(object sender, TArgs args)
    {
        Sender = sender;
        Args = args;
    }

    public object Sender { get; }

    public bool Handled { get; set; }

    public TArgs Args { get; }
}