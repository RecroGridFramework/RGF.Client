using System.Net;

namespace Recrovit.RecroGridFramework.Client.Services;

public sealed class RgfAuthenticationFailureContext
{
    public const string ReauthenticationRequiredHeaderName = "X-Recrovit-Auth";

    public const string ReauthenticationRequiredHeaderValue = "reauth-required";

    public required HttpStatusCode StatusCode { get; init; }

    public string? RequestUri { get; init; }

    public bool IsReauthenticationRequired { get; init; }

    public IReadOnlyDictionary<string, string[]> ResponseHeaders { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
}
