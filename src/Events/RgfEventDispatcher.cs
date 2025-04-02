using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Abstraction.Infrastructure.Events;
using Recrovit.RecroGridFramework.Client.Services;

namespace Recrovit.RecroGridFramework.Client.Events;

public class RgfEventDispatcher<TEvent, TArgs> where TEvent : notnull where TArgs : EventArgs
{

    public RgfEventDispatcher()
    {
        _logger = RgfLoggerFactory.GetLogger<RgfEventDispatcher<TEvent, TArgs>>();
    }

    private readonly ILogger? _logger;

    private Dictionary<TEvent, EventDispatcher<IRgfEventArgs<TArgs>>> _eventHandlers = [];

    private Dictionary<TEvent, EventDispatcher<IRgfEventArgs<TArgs>>> _defaultHandlers = [];

    private EventDispatcher<IRgfEventArgs<TArgs>> _genericHandler = new();

    private List<(object Subscriber, object Handler)> _anonymousHandlers = new();

    public Func<IRgfEventArgs<TArgs>, Task> Subscribe(TEvent eventName, Func<IRgfEventArgs<TArgs>, Task> handler, object? subscriberTarget = null) => Subscribe(eventName, handler, false, subscriberTarget);
    public Func<IRgfEventArgs<TArgs>, Task> Subscribe(TEvent eventName, Func<IRgfEventArgs<TArgs>, Task> handler, bool useDefaultHandler, object? subscriberTarget = null)
    {
        EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
        if (useDefaultHandler)
        {
            if (!_defaultHandlers.TryGetValue(eventName, out handlers))
            {
                handlers = new(_logger);
                _defaultHandlers[eventName] = handlers;
            }
        }
        else
        {
            if (!_eventHandlers.TryGetValue(eventName, out handlers))
            {
                handlers = new(_logger);
                _eventHandlers.Add(eventName, handlers);
            }
        }

        handlers.Subscribe(handler);

        if (handler.Target == null && subscriberTarget != null)
        {
            _anonymousHandlers.Add(new(subscriberTarget, handler));
        }

        return handler;
    }

    public Action<IRgfEventArgs<TArgs>> Subscribe(TEvent eventName, Action<IRgfEventArgs<TArgs>> handler, object? subscriberTarget = null) => Subscribe(eventName, handler, false, subscriberTarget);
    public Action<IRgfEventArgs<TArgs>> Subscribe(TEvent eventName, Action<IRgfEventArgs<TArgs>> handler, bool useDefaultHandler, object? subscriberTarget = null)
    {
        EventDispatcher<IRgfEventArgs<TArgs>>? handlers;
        if (useDefaultHandler)
        {
            if (!_defaultHandlers.TryGetValue(eventName, out handlers))
            {
                handlers = new();
                _defaultHandlers[eventName] = handlers;
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

        if (handler.Target == null && subscriberTarget != null)
        {
            _anonymousHandlers.Add(new(subscriberTarget, handler));
        }

        return handler;
    }

    public Func<IRgfEventArgs<TArgs>, Task> Subscribe(TEvent[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler, object? subscriberTarget = null) => Subscribe(eventNames, handler, false, subscriberTarget);
    public Func<IRgfEventArgs<TArgs>, Task> Subscribe(TEvent[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler, bool useDefaultHandler, object? subscriberTarget = null)
    {
        Array.ForEach(eventNames, (e) => Subscribe(e, handler, useDefaultHandler, subscriberTarget));
        return handler;
    }

    public Action<IRgfEventArgs<TArgs>> Subscribe(TEvent[] eventNames, Action<IRgfEventArgs<TArgs>> handler, object? subscriberTarget = null) => Subscribe(eventNames, handler, false, subscriberTarget);
    public Action<IRgfEventArgs<TArgs>> Subscribe(TEvent[] eventNames, Action<IRgfEventArgs<TArgs>> handler, bool useDefaultHandler, object? subscriberTarget = null)
    {
        Array.ForEach(eventNames, (e) => Subscribe(e, handler, useDefaultHandler, subscriberTarget));
        return handler;
    }

    public Func<IRgfEventArgs<TArgs>, Task> Subscribe(Func<IRgfEventArgs<TArgs>, Task> handler, object? subscriberTarget = null)
    {
        _genericHandler.Subscribe(handler);

        if (handler.Target == null && subscriberTarget != null)
        {
            _anonymousHandlers.Add(new(subscriberTarget, handler));
        }

        return handler;
    }


    public void Unsubscribe(TEvent eventName, Func<IRgfEventArgs<TArgs>, Task> handler)
    {
        if (handler != null)
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Unsubscribe(handler);
            }
            if (_defaultHandlers.TryGetValue(eventName, out var dhandlers))
            {
                dhandlers.Unsubscribe(handler);
            }
        }
    }

    public void Unsubscribe(TEvent eventName, Action<IRgfEventArgs<TArgs>> handler)
    {
        if (handler != null)
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Unsubscribe(handler);
            }
            if (_defaultHandlers.TryGetValue(eventName, out var dhandlers))
            {
                dhandlers.Unsubscribe(handler);
            }
        }
    }

    public void Unsubscribe(Func<IRgfEventArgs<TArgs>, Task> handler)
    {
        foreach (var dispatcher in _eventHandlers.Values)
        {
            dispatcher.Unsubscribe(handler);
        }
        foreach (var dispatcher in _defaultHandlers.Values)
        {
            dispatcher.Unsubscribe(handler);
        }
        _genericHandler.Unsubscribe(handler);
    }

    public void Unsubscribe(Action<IRgfEventArgs<TArgs>> handler)
    {
        foreach (var dispatcher in _eventHandlers.Values)
        {
            dispatcher.Unsubscribe(handler);
        }
        foreach (var dispatcher in _defaultHandlers.Values)
        {
            dispatcher.Unsubscribe(handler);
        }
        _genericHandler.Unsubscribe(handler);
    }

    public void Unsubscribe(object subscriberTarget)
    {
        foreach (var target in _anonymousHandlers.Where((e) => e.Subscriber == subscriberTarget).ToArray())
        {
            if (target.Handler is Func<IRgfEventArgs<TArgs>, Task> asyncHandler)
            {
                Unsubscribe(asyncHandler);
            }
            else if (target.Handler is Action<IRgfEventArgs<TArgs>> handler)
            {
                Unsubscribe(handler);
            }
            _anonymousHandlers.Remove(target);
        }

        foreach (var dispatcher in _eventHandlers.Values)
        {
            dispatcher.Unsubscribe(subscriberTarget);
        }
        foreach (var dispatcher in _defaultHandlers.Values)
        {
            dispatcher.Unsubscribe(subscriberTarget);
        }
        _genericHandler.Unsubscribe(subscriberTarget);
    }

    public void Unsubscribe(TEvent[] eventNames, Func<IRgfEventArgs<TArgs>, Task> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));

    public void Unsubscribe(TEvent[] eventNames, Action<IRgfEventArgs<TArgs>> handler) => Array.ForEach(eventNames, (e) => Unsubscribe(e, handler));


    public async Task<bool> DispatchEventAsync(TEvent eventName, IRgfEventArgs<TArgs> args)
    {
        _logger?.LogDebug("DispatchEvent | EventName:{eventName}, Sender:{senderType}", eventName, args.Sender?.GetType().Name);

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

    public Task<bool> RaiseEventAsync(TEvent eventKind, object sender)
    {
        var eventArgs = Activator.CreateInstance(typeof(TArgs), eventKind);
        var args = Activator.CreateInstance(typeof(RgfEventArgs<TArgs>), sender, eventArgs);
        return DispatchEventAsync(eventKind, (IRgfEventArgs<TArgs>)args!);
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

    public DateTime TriggeredAt { get; } = DateTime.Now;

    public TArgs Args { get; }

    public bool Handled { get; set; }

    public bool PreventDefault { get; set; }
}