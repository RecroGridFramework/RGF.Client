namespace Recrovit.RecroGridFramework.Client.Services;

public interface IRgfAuthenticationFailureHandler
{
    ValueTask HandleUnauthorizedAsync(RgfAuthenticationFailureContext context, CancellationToken cancellationToken);
}

public sealed class NoOpRgfAuthenticationFailureHandler : IRgfAuthenticationFailureHandler
{
    public ValueTask HandleUnauthorizedAsync(RgfAuthenticationFailureContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
