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
    private SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private int _lockThreadId;

    private readonly List<EventCallback<ObservablePropertyEventArgs<TItem>>> _afterChangeCallbacks = [];
    private readonly List<EventCallback<ObservablePropertyEventArgs<TItem>>> _beforeChangeCallbacks = [];

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

    public IDisposable OnBeforeChange(object receiver, Action<ObservablePropertyEventArgs<TItem>> handler)
    {
        var callback = EventCallback.Factory.Create(receiver, handler);
        _beforeChangeCallbacks.Add(callback);
        return new DisposableCallback(this, callback);
    }

    public IDisposable OnAfterChange(object receiver, Action<ObservablePropertyEventArgs<TItem>> handler)
    {
        var callback = EventCallback.Factory.Create(receiver, handler);
        _afterChangeCallbacks.Add(callback);
        return new DisposableCallback(this, callback);
    }

    public IDisposable OnAfterChange(object receiver, Func<ObservablePropertyEventArgs<TItem>, Task> handler)
    {
        var callback = EventCallback.Factory.Create(receiver, handler);
        _afterChangeCallbacks.Add(callback);
        return new DisposableCallback(this, callback);
    }

    public void Unsubscribe(IDisposable callback)
    {
        var cb = callback as DisposableCallback;
        if (cb != null)
        {
            _beforeChangeCallbacks.Remove(cb.Callback);
            _afterChangeCallbacks.Remove(cb.Callback);
        }
    }

    private Task NotifyBeforeChangeAsync(ObservablePropertyEventArgs<TItem> args)
    {
        if (_beforeChangeCallbacks.Any())
        {
            var tasks = _beforeChangeCallbacks.Select(callback => callback.InvokeAsync(args)).ToArray();
            return Task.WhenAll(tasks);
        }
        return Task.CompletedTask;
    }

    public Task SendChangeNotificationAsync(ObservablePropertyEventArgs<TItem> args)
    {
        if (_afterChangeCallbacks.Any())
        {
            var tasks = _afterChangeCallbacks.Select(callback => callback.InvokeAsync(args)).ToArray();
            return Task.WhenAll(tasks);
        }
        return Task.CompletedTask;
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
        public DisposableCallback(ObservableProperty<TItem> property, EventCallback<ObservablePropertyEventArgs<TItem>> callback)
        {
            Property = property;
            Callback = callback;
        }

        public ObservableProperty<TItem> Property { get; }

        public EventCallback<ObservablePropertyEventArgs<TItem>> Callback { get; }

        public void Dispose() => Property.Unsubscribe(this);
    }
}