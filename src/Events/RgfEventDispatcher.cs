using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Events;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfEventDispatcher<TEvent, TArgs> where TEvent : notnull where TArgs : EventArgs
{
    private Dictionary<TEvent, EventDispatcher<IRgfEventArgs<TArgs>>> _eventHandlers = [];

    private Dictionary<TEvent, EventDispatcher<IRgfEventArgs<TArgs>>> _defaultHandlers = [];

    private EventDispatcher<IRgfEventArgs<TArgs>> _genericHandler = new();

    public void Subscribe(TEvent eventName, Func<IRgfEventArgs<TArgs>, Task> handler, bool defaultHandler = false)
    {
        if (handler != null)
        {
            EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
            if (defaultHandler)
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

    public void Subscribe(TEvent eventName, Action<IRgfEventArgs<TArgs>> handler)
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

    public void Unsubscribe(TEvent eventName, Func<IRgfEventArgs<TArgs>, Task> handler)
    {
        if (handler != null && _eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Unsubscribe(handler);
        }
    }

    public void Unsubscribe(TEvent eventName, Action<IRgfEventArgs<TArgs>> handler)
    {
        if (handler != null && _eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Unsubscribe(handler);
        }
    }

    public async Task<bool> DispatchEventAsync(TEvent eventName, IRgfEventArgs<TArgs> args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            await handlers.InvokeAsync(args);
        }

        await _genericHandler.InvokeAsync(args);

        if (!args.PreventDefault && _defaultHandlers.TryGetValue(eventName, out var defaultHandlers))
        {
            await defaultHandlers.InvokeAsync(args);
        }
        return args.Handled;
    }

    public void Subscribe(TEvent[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler, bool defaultHandler = false) => Array.ForEach(eventNames, (e) => Subscribe(e, handler, defaultHandler));
    public void Subscribe(TEvent[] eventNames, Action<IRgfEventArgs<TArgs>> handler) => Array.ForEach(eventNames, (e) => Subscribe(e, handler));
    public void Subscribe(Func<IRgfEventArgs<TArgs>, Task> handler) =>  _genericHandler.Subscribe(handler);
    public void Unsubscribe(TEvent[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));
    public void Unsubscribe(TEvent[] eventNames, Action<IRgfEventArgs<TArgs>> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));
    public void Unsubscribe(Func<IRgfEventArgs<TArgs>, Task> handler) => _genericHandler.Unsubscribe(handler);
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

    public bool PreventDefault { get; set; }

    public TArgs Args { get; }
}