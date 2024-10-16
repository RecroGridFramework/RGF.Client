using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Services;
using Recrovit.RecroGridFramework.Client.Events;
using System.Collections.Concurrent;

namespace Recrovit.RecroGridFramework.Client.Services;

internal class RgfEventNotificationService : IRgfEventNotificationService
{
    private readonly ILogger<RgfEventNotificationService> _logger;

    public RgfEventNotificationService(ILogger<RgfEventNotificationService> logger)
    {
        this._logger = logger;
    }

    private ConcurrentDictionary<string, RgfNotificationManager> _scope = new();

    public IRgfNotificationManager GetNotificationManager(string scope) => _scope.GetOrAdd(scope, e => new RgfNotificationManager(_logger, this, scope));

    public Task RaiseEvent<TArgs>(string scope, TArgs args, object sender) where TArgs : EventArgs => GetNotificationManager(scope).RaiseEventAsync<TArgs>(args, sender);

    public IRgfObserver<IRgfEventArgs<TArgs>> Subscribe<TArgs>(string scope, Action<IRgfEventArgs<TArgs>> handler) where TArgs : EventArgs => GetNotificationManager(scope).Subscribe<TArgs>(handler);

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

    private ConcurrentDictionary<string, object> _observableEvent = new();

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

    public Task RaiseEventAsync<TArgs>(TArgs args, object sender) where TArgs : EventArgs
    {
        var observable = GetObservableEvents<TArgs>();
        return observable.RaiseEventAsync(new RgfEventArgs<TArgs>(sender, args));
    }

    public IRgfObserver<IRgfEventArgs<TArgs>> Subscribe<TArgs>(Action<IRgfEventArgs<TArgs>> handler) where TArgs : EventArgs
    {
        var observable = (RgfObservableEvent<TArgs>)GetObservableEvents<TArgs>();
        var observer = new RgfObserver<TArgs>(handler);
        observer.Subscribe(observable);
        return observer;
    }

    public IRgfObserver<IRgfEventArgs<TArgs>> Subscribe<TArgs>(Func<IRgfEventArgs<TArgs>, Task> handler) where TArgs : EventArgs
    {
        var observable = (RgfObservableEvent<TArgs>)GetObservableEvents<TArgs>();
        var observer = new RgfObserver<TArgs>(handler);
        observer.Subscribe(observable);
        return observer;
    }


    private class RgfObservableEvent<TArgs> : IRgfObservableEvent<TArgs>, IRgfObservable<IRgfEventArgs<TArgs>> where TArgs : EventArgs
    {
        private readonly ILogger _logger;

        public RgfObservableEvent(ILogger logger)
        {
            this._logger = logger;
        }

        private List<IRgfObserver<IRgfEventArgs<TArgs>>> _observers = new();

        private class Unsubscriber : IDisposable
        {
            private List<IRgfObserver<IRgfEventArgs<TArgs>>> _observers;
            private IRgfObserver<IRgfEventArgs<TArgs>> _observer;

            public Unsubscriber(List<IRgfObserver<IRgfEventArgs<TArgs>>> observers, IRgfObserver<IRgfEventArgs<TArgs>> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        [Obsolete("Use instead Subscribe(IRgfObserver<IRgfEventArgs<TArgs>>)", true)]
        public IDisposable Subscribe(IObserver<IRgfEventArgs<TArgs>> observer) => Subscribe((IRgfObserver<IRgfEventArgs<TArgs>>)(observer));

        public IDisposable Subscribe(IRgfObserver<IRgfEventArgs<TArgs>> observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
            return new Unsubscriber(_observers, observer);
        }

        public async Task RaiseEventAsync(IRgfEventArgs<TArgs> args)
        {
            _logger.LogDebug("{EventType}: {Args}", typeof(TArgs), args?.Args?.ToString());
            foreach (var observer in _observers)
            {
                await observer.OnNextAsync(args!);
            }
        }
    }

    private class RgfObserver<TArgs> : IRgfObserver<IRgfEventArgs<TArgs>> where TArgs : EventArgs
    {
        public RgfObserver(Action<IRgfEventArgs<TArgs>> handler)
        {
            _handler = handler;
        }

        public RgfObserver(Func<IRgfEventArgs<TArgs>, Task> handler)
        {
            _handlerAsync = handler;
        }

        private IDisposable? unsubscriber = null;
        private readonly Action<IRgfEventArgs<TArgs>>? _handler;
        private readonly Func<IRgfEventArgs<TArgs>, Task>? _handlerAsync;

        public void Subscribe(IRgfObservable<IRgfEventArgs<TArgs>> provider)
        {
            unsubscriber = provider.Subscribe(this);
        }

        public void Unsubscribe() => unsubscriber?.Dispose();

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        [Obsolete("Use instead Subscribe(IRgfObserver<IRgfEventArgs<TArgs>>)", true)]
        public void OnNext(IRgfEventArgs<TArgs> value) => OnNextAsync(value);

        public Task OnNextAsync(IRgfEventArgs<TArgs> value)
        {
            _handler?.Invoke(value);
            return _handlerAsync?.Invoke(value) ?? Task.CompletedTask;
        }

        public void Dispose() => Unsubscribe();
    }
}