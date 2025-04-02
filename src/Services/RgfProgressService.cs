using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Services;

public interface IRgfProgressService : IAsyncDisposable
{
    string? ConnectionId { get; }

    Task<string?> StartAsync(int inactivityTimeoutMinutes = 60, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    void AddToBackgroundInstances();

    event Action<IRgfProgressService, IRgfProgressArgs> OnProgressChanged;

    event Func<IRgfProgressService, IRgfProgressArgs, Task> OnProgressChangedAsync;
}

public class RgfProgressService : IRgfProgressService
{
    private readonly ILogger<RgfProgressService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public RgfProgressService(ILogger<RgfProgressService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private HubConnection? _hubConnection;
    private DateTime _lastActivity = DateTime.Now;
    private int _inactivityTimeoutMinutes;

    private static readonly List<RgfProgressService> _backgroundProgressInstances = [];
    private static readonly object _lock = new();

    public string? ConnectionId => _hubConnection?.ConnectionId;

    public event Action<IRgfProgressService, IRgfProgressArgs> OnProgressChanged = null!;
    public event Func<IRgfProgressService, IRgfProgressArgs, Task> OnProgressChangedAsync = null!;

    public async Task<string?> StartAsync(int inactivityTimeoutMinutes = 60, CancellationToken cancellationToken = default)
    {
        _inactivityTimeoutMinutes = inactivityTimeoutMinutes;
        var uri = new Uri($"{ApiService.BaseAddress.TrimEnd('/')}{RgfSignalR.RgfProgressHubEndpoint}");
        _logger.LogDebug("Starting SignalR connection to {Uri} with inactivity timeout of {TimeoutMinutes} minutes", uri.AbsoluteUri, _inactivityTimeoutMinutes);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(uri.AbsoluteUri)
            //.AddJsonProtocol(options => options.PayloadSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) )
            .Build();

        _hubConnection.On<RgfProgressArgs>(nameof(IRgfProgressHub.ReceiveProgress), (p) =>
        {
            _lastActivity = DateTime.Now;
            OnProgressChanged?.Invoke(this, p);
        });

        _hubConnection.On<RgfProgressArgs>(nameof(IRgfProgressHub.ReceiveProgress), (p) =>
        {
            _lastActivity = DateTime.Now;
            return OnProgressChangedAsync?.Invoke(this, p) ?? Task.CompletedTask;
        });

        await _hubConnection.StartAsync(cancellationToken);

        _lastActivity = DateTime.Now;
        _ = CheckForConnectionInactivity();

        _logger.LogDebug("SignalR connection started: {ConnectionId}", _hubConnection.ConnectionId);

        return _hubConnection.ConnectionId;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _backgroundProgressInstances.Remove(this);
        }

        if (_hubConnection?.ConnectionId == null)
        {
            return Task.CompletedTask;
        }

        _logger.LogDebug("Stopping SignalR connection: {ConnectionId}", _hubConnection.ConnectionId);
        return _hubConnection.StopAsync(cancellationToken);
    }

    private async Task CheckForConnectionInactivity()
    {
        await Task.Delay(TimeSpan.FromMinutes(_inactivityTimeoutMinutes));

        if (_hubConnection?.ConnectionId != null)
        {
            if (_lastActivity.AddMinutes(_inactivityTimeoutMinutes) > DateTime.Now)
            {
                _ = CheckForConnectionInactivity();
                return;
            }
            _logger.LogWarning("SignalR connection inactivity timeout reached: {ConnectionId}", _hubConnection?.ConnectionId);
            await DisposeAsync();
        }
    }

    public void AddToBackgroundInstances()
    {
        lock (_lock)
        {
            _backgroundProgressInstances.Add(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await StopAsync();
            _logger.LogDebug("Disposing SignalR connection");
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}