using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Recrovit.RecroGridFramework.Abstraction.Contracts.Constants;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Services;

public interface IRgfProgressService : IAsyncDisposable
{
    string? ConnectionId { get; }

    Task<string?> StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    event Action<IRgfProgressArgs> OnProgressChanged;
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

    public string? ConnectionId => _hubConnection?.ConnectionId;

    public event Action<IRgfProgressArgs> OnProgressChanged = null!;

    public async Task<string?> StartAsync(CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"{ApiService.BaseAddress.TrimEnd('/')}{RgfSignalR.RgfProgressHubEndpoint}");
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(uri.AbsoluteUri)
            //.AddJsonProtocol(options => options.PayloadSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) )
            .Build();

        _hubConnection.On<RgfProgressArgs>(nameof(IRgfProgressHub.ReceiveProgress), (p) => OnProgressChanged?.Invoke(p));

        await _hubConnection.StartAsync(cancellationToken);

        return _hubConnection.ConnectionId;
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => _hubConnection?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
    }
}