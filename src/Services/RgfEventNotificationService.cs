using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Events;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RgfEventNotificationService : IRgfEventNotificationService
{
    private readonly ILogger<RgfEventNotificationService> _logger;

    public RgfEventNotificationService(ILogger<RgfEventNotificationService> logger)
    {
        this._logger = logger;
    }

    private ConcurrentDictionary<string, RgfNotificationManager> _scope { get; set; } = new();

    public IRgfNotificationManager GetNotificationManager(string scope) => _scope.GetOrAdd(scope, e => new RgfNotificationManager(_logger, this, scope));

    public void RaiseEvent<TArgs>(string scope, TArgs args, object sender) where TArgs : EventArgs => GetNotificationManager(scope).RaiseEvent<TArgs>(args, sender);

    public IRgfObserver Subscribe<TArgs>(string scope, object receiver, Action<IRgfEventArgs<TArgs>> handler) where TArgs : EventArgs => GetNotificationManager(scope).Subscribe<TArgs>(receiver, handler);

    public bool RemoveNotificationManager(string scope) => _scope.Remove(scope, out _);
}

internal class RgfNotificationManager : IRgfNotificationManager
{
    private readonly ILogger _logger;
    private readonly IRgfEventNotificationService? _notificationService;
    private readonly string? _scope;

    public RgfNotificationManager(IServiceProvider serviceProvider) : this(serviceProvider.GetRequiredService<ILogger<RgfNotificationManager>>()) { }
    public RgfNotificationManager(ILogger logger, IRgfEventNotificationService? notificationService = null, string? scope = null)
    {
        _logger = logger;
        _notificationService = notificationService;
        _scope = scope;
    }

    private ConcurrentDictionary<string, object> _observableEvent { get; set; } = new();

    public void Dispose()
    {
        if (_notificationService != null && !string.IsNullOrEmpty(_scope))
        {
            _notificationService.RemoveNotificationManager(_scope);
        }
    }

    public IRgfObservableEvent<TArgs> GetObservableEvents<TArgs>() where TArgs : EventArgs
    {
        string? key = typeof(TArgs).FullName;
        if (key == null)
        {
            throw new InvalidOperationException("AssemblyQualifiedName is null for the specified type.");
        }
        var observableEvent = _observableEvent.GetOrAdd(key, argType =>
        {
            return new RgfObservableEvent<TArgs>(_logger);
        });
        return (RgfObservableEvent<TArgs>)observableEvent;
    }

    public void RaiseEvent<TArgs>(TArgs args, object sender) where TArgs : EventArgs
    {
        var observable = GetObservableEvents<TArgs>();
        observable.RaiseEvent(new RgfEventArgs<TArgs>(sender, args));
    }

    public IRgfObserver Subscribe<TArgs>(object receiver, Action<IRgfEventArgs<TArgs>> handler) where TArgs : EventArgs
    {
        var observable = (RgfObservableEvent<TArgs>)GetObservableEvents<TArgs>();
        var observer = new RgfObserver<TArgs>(receiver, handler);
        observer.Subscribe(observable);
        return observer;
    }
    public IRgfObserver Subscribe<TArgs>(object receiver, Func<IRgfEventArgs<TArgs>, Task> handler) where TArgs : EventArgs
    {
        var observable = (RgfObservableEvent<TArgs>)GetObservableEvents<TArgs>();
        var observer = new RgfObserver<TArgs>(receiver, handler);
        observer.Subscribe(observable);
        return observer;
    }


    private class RgfObservableEvent<TArgs> : IRgfObservableEvent<TArgs>, IObservable<IRgfEventArgs<TArgs>> where TArgs : EventArgs
    {
        private readonly ILogger _logger;

        public RgfObservableEvent(ILogger logger)
        {
            this._logger = logger;
        }
        private List<IObserver<IRgfEventArgs<TArgs>>> observers { get; } = new();

        private class Unsubscriber : IDisposable
        {
            private List<IObserver<IRgfEventArgs<TArgs>>> _observers;
            private IObserver<IRgfEventArgs<TArgs>> _observer;

            public Unsubscriber(List<IObserver<IRgfEventArgs<TArgs>>> observers, IObserver<IRgfEventArgs<TArgs>> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        public IDisposable Subscribe(IObserver<IRgfEventArgs<TArgs>> observer)
        {
            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }
            return new Unsubscriber(observers, observer);
        }

        public void RaiseEvent(IRgfEventArgs<TArgs> args)
        {
            _logger.LogDebug("{EventType}: {Args}", typeof(TArgs), args?.Args?.ToString());
            foreach (var observer in observers)
            {
#pragma warning disable CS8604 // Possible null reference argument.
                observer.OnNext(args);
#pragma warning restore CS8604 // Possible null reference argument.
            }
        }
    }

    private class RgfObserver<TArgs> : IRgfObserver, IObserver<IRgfEventArgs<TArgs>> where TArgs : EventArgs
    {
        public RgfObserver(object receiver, Action<IRgfEventArgs<TArgs>> handler)
        {
            _callback = EventCallback.Factory.Create(receiver, handler);
        }
        public RgfObserver(object receiver, Func<IRgfEventArgs<TArgs>, Task> handler)
        {
            _callback = EventCallback.Factory.Create(receiver, handler);
        }

        private IDisposable? unsubscriber = null;
        private EventCallback<IRgfEventArgs<TArgs>> _callback = new();

        public void Subscribe(IObservable<IRgfEventArgs<TArgs>> provider)
        {
            unsubscriber = provider.Subscribe(this);
        }
        public void Unsubscribe() => unsubscriber?.Dispose();
        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void OnNext(IRgfEventArgs<TArgs> value) => _callback.InvokeAsync(value);

        public void Dispose() => Unsubscribe();
    }
}