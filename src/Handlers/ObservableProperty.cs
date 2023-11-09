using Microsoft.AspNetCore.Components;
using System;

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
        _name = name;
    }

    private TItem _value;
    private readonly object _lock = new object();

    private List<EventCallback<ObservablePropertyEventArgs<TItem>>> _afterChangeCallbacks = new();
    private List<EventCallback<ObservablePropertyEventArgs<TItem>>> _beforeChangeCallbacks = new();

    public TItem Value
    {
        get => _value;
        set => SetValue(value);
    }
    public string? _name { get; }

    private void SetValue(TItem newData)
    {
        lock (_lock)
        {
            bool eq = typeof(IEquatable<TItem>).IsAssignableFrom(typeof(TItem));
            if (!eq || !Equals(newData, _value))
            {
                var args = new ObservablePropertyEventArgs<TItem>(_value, newData);
                NotifyBeforeChange(args);
                _value = newData;
                _ = SendChangeNotificationAsync(args);
            }
        }
    }

    public void ModifySilently(TItem newValue)
    {
        lock (_lock)
        {
            _value = newValue;
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

    private void NotifyBeforeChange(ObservablePropertyEventArgs<TItem> args)
    {
        foreach (var fn in _beforeChangeCallbacks)
        {
            fn.InvokeAsync(args);
        }
    }

    public async Task SendChangeNotificationAsync(ObservablePropertyEventArgs<TItem> args)
    {
        foreach (var callback in _afterChangeCallbacks)
        {
            await callback.InvokeAsync(args);
        }
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
