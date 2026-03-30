namespace Recrovit.RecroGridFramework.Client.Handlers;

public interface IRgfAccessTokenAccessor
{
    Task<string?> GetAccessTokenAsync();
}

public sealed class NoOpRgfAccessTokenAccessor : IRgfAccessTokenAccessor
{
    public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>(null);
}
