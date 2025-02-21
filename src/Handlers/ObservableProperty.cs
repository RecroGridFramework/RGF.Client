using Microsoft.AspNetCore.Components;

namespace Recrovit.RecroGridFramework.Client.Handlers;

public class ObservablePropertyEventArgs<TItem>
{
    public ObservablePropertyEventArgs(TItem origData, TItem newData)
    {
        OrigData = origData;
        NewData = newData;
    }

    public TItem OrigData { get; private set; }

    public TItem NewData { get; private set; }
}

public class ObservableProperty<TItem>
{
    public ObservableProperty(TItem value, string? name = null)
    {
        _value = value;
        Name = name;
    }

    private TItem _value;
    private readonly object _thisLock = new object();
    private SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private int _lockThreadId;

    private readonly List<EventCallback<ObservablePropertyEventArgs<TItem>>> _afterChangeCallbacks = [];
    private readonly List<EventCallback<ObservablePropertyEventArgs<TItem>>> _beforeChangeCallbacks = [];
    private readonly List<Action<ObservablePropertyEventArgs<TItem>>> _afterChangeActions = [];
    private readonly List<Action<ObservablePropertyEventArgs<TItem>>> _beforeChangeActions = [];
    private readonly List<Func<ObservablePropertyEventArgs<TItem>, Task>> _afterChangeTasks = [];
    private readonly List<Func<ObservablePropertyEventArgs<TItem>, Task>> _beforeChangeTasks = [];

    public TItem Value
    {
        get => _value;
        set => _ = SetValueAsync(value);
    }

    public string? Name { get; }

    public async Task SetValueAsync(TItem newData)
    {
        await Lock();
        try
        {
            bool eq = typeof(IEquatable<TItem>).IsAssignableFrom(typeof(TItem));
            if (!eq || !Equals(newData, _value))
            {
                var args = new ObservablePropertyEventArgs<TItem>(_value, newData);
                await NotifyBeforeChangeAsync(args);
                _value = newData;
                await SendChangeNotificationAsync(args);
            }
        }
        finally
        {
            Unlock();
        }
    }

    public async Task ModifySilentlyAsync(TItem newValue)
    {
        await Lock();
        try
        {
            _value = newValue;
        }
        finally
        {
            Unlock();
        }
    }

    public IDisposable OnBeforeChange(object receiver, Action<ObservablePropertyEventArgs<TItem>> action)
    {
        var callback = EventCallback.Factory.Create(receiver, action);
        lock (_thisLock)
        {
            _beforeChangeCallbacks.Add(callback);
        }
        return new DisposableCallback(this, callback);
    }

    public IDisposable OnBeforeChange(object receiver, Func<ObservablePropertyEventArgs<TItem>, Task> task)
    {
        var callback = EventCallback.Factory.Create(receiver, task);
        lock (_thisLock)
        {
            _beforeChangeCallbacks.Add(callback);
        }
        return new DisposableCallback(this, callback);
    }

    public IDisposable OnAfterChange(object receiver, Action<ObservablePropertyEventArgs<TItem>> action)
    {
        var callback = EventCallback.Factory.Create(receiver, action);
        lock (_thisLock)
        {
            _afterChangeCallbacks.Add(callback);
        }
        return new DisposableCallback(this, callback);
    }

    public IDisposable OnAfterChange(object receiver, Func<ObservablePropertyEventArgs<TItem>, Task> task)
    {
        var callback = EventCallback.Factory.Create(receiver, task);
        lock (_thisLock)
        {
            _afterChangeCallbacks.Add(callback);
        }
        return new DisposableCallback(this, callback);
    }

    public void Unsubscribe(IDisposable callback)
    {
        if (callback is DisposableCallback cb)
        {
            lock (_thisLock)
            {
                if (cb.Callback != null)
                {
                    _beforeChangeCallbacks.Remove(cb.Callback.Value);
                    _afterChangeCallbacks.Remove(cb.Callback.Value);
                }
                else if (cb.Task != null)
                {
                    _beforeChangeTasks.Remove(cb.Task);
                    _afterChangeTasks.Remove(cb.Task);
                }
                else if (cb.Action != null)
                {
                    _beforeChangeActions.Remove(cb.Action);
                    _afterChangeActions.Remove(cb.Action);
                }
            }
        }
    }

    private static readonly string TargetNullErrorMessage =
        "This method can only be used with instance methods. " +
        "For static methods or lambdas, use OnAfterChange/OnBeforeChange with an explicit receiver.";

    public IDisposable SubscribeBeforeChange(Action<ObservablePropertyEventArgs<TItem>> action)
    {
        if (action.Target == null) throw new InvalidOperationException(TargetNullErrorMessage);
        lock (_thisLock)
        {
            _beforeChangeActions.Remove(action);
            _beforeChangeActions.Add(action);
        }
        return new DisposableCallback(this, action);
    }

    public void UnsubscribeBeforeChange(Action<ObservablePropertyEventArgs<TItem>> action) { lock (_thisLock) { _beforeChangeActions.Remove(action); } }

    public IDisposable SubscribeBeforeChange(Func<ObservablePropertyEventArgs<TItem>, Task> task)
    {
        if (task.Target == null) throw new InvalidOperationException(TargetNullErrorMessage);
        lock (_thisLock)
        {
            _beforeChangeTasks.Remove(task);
            _beforeChangeTasks.Add(task);
        }
        return new DisposableCallback(this, task);
    }

    public void UnsubscribeBeforeChange(Func<ObservablePropertyEventArgs<TItem>, Task> task) { lock (_thisLock) { _beforeChangeTasks.Remove(task); } }

    public IDisposable SubscribeAfterChange(Action<ObservablePropertyEventArgs<TItem>> action)
    {
        if (action.Target == null) throw new InvalidOperationException(TargetNullErrorMessage);
        lock (_thisLock)
        {
            _afterChangeActions.Remove(action);
            _afterChangeActions.Add(action);
        }
        return new DisposableCallback(this, action);
    }

    public void UnsubscribeAfterChange(Action<ObservablePropertyEventArgs<TItem>> action) { lock (_thisLock) { _afterChangeActions.Remove(action); } }

    public IDisposable SubscribeAfterChange(Func<ObservablePropertyEventArgs<TItem>, Task> task)
    {
        if (task.Target == null) throw new InvalidOperationException(TargetNullErrorMessage);
        lock (_thisLock)
        {
            _afterChangeTasks.Remove(task);
            _afterChangeTasks.Add(task);
        }
        return new DisposableCallback(this, task);
    }

    public void UnsubscribeAfterChange(Func<ObservablePropertyEventArgs<TItem>, Task> task) { lock (_thisLock) { _afterChangeTasks.Remove(task); } }


    private async Task NotifyBeforeChangeAsync(ObservablePropertyEventArgs<TItem> args)
    {
        foreach (var action in _beforeChangeActions)
        {
            action.Invoke(args);
        }

        var tasks = _beforeChangeCallbacks.Select(callback => callback.InvokeAsync(args))
            .Concat(_beforeChangeTasks.Select(task => task.Invoke(args)))
            .ToArray();

        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    public async Task SendChangeNotificationAsync(ObservablePropertyEventArgs<TItem> args)
    {
        foreach (var action in _afterChangeActions)
        {
            action.Invoke(args);
        }

        var tasks = _afterChangeCallbacks.Select(callback => callback.InvokeAsync(args))
            .Concat(_afterChangeTasks.Select(task => task.Invoke(args)))
            .ToArray();

        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks);
        }
    }


    private async Task<bool> Lock()
    {
        if (Thread.CurrentThread.ManagedThreadId != _lockThreadId)
        {
            await _lock.WaitAsync();
            _lockThreadId = Thread.CurrentThread.ManagedThreadId;
        }
        return true;
    }

    private void Unlock()
    {
        if (Thread.CurrentThread.ManagedThreadId != _lockThreadId)
        {
            throw new InvalidOperationException();
        }
        _lockThreadId = 0;
        _lock.Release();
    }


    private class DisposableCallback : IDisposable
    {
        public DisposableCallback(ObservableProperty<TItem> property, Func<ObservablePropertyEventArgs<TItem>, Task> task)
        {
            Property = property;
            Task = task;
        }

        public DisposableCallback(ObservableProperty<TItem> property, EventCallback<ObservablePropertyEventArgs<TItem>> callback)
        {
            Property = property;
            Callback = callback;
        }

        public DisposableCallback(ObservableProperty<TItem> property, Action<ObservablePropertyEventArgs<TItem>> action)
        {
            Property = property;
            Action = action;
        }

        public ObservableProperty<TItem> Property { get; }

        public EventCallback<ObservablePropertyEventArgs<TItem>>? Callback { get; }

        public Func<ObservablePropertyEventArgs<TItem>, Task>? Task { get; }

        public Action<ObservablePropertyEventArgs<TItem>>? Action { get; }

        public void Dispose() => Property.Unsubscribe(this);
    }
}