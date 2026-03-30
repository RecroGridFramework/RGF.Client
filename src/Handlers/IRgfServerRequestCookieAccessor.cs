namespace Recrovit.RecroGridFramework.Client.Handlers;

/// <summary>
/// Provides the incoming request cookie header for SSR proxy calls.
/// </summary>
public interface IRgfServerRequestCookieAccessor
{
    string? GetCookieHeader();
}
