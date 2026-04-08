using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Recrovit.RecroGridFramework.Client.Services;

internal sealed class RgfAuthenticationPrincipalSnapshot
{
    public bool IsAuthenticated { get; init; }

    public IReadOnlyList<RgfAuthenticationIdentitySnapshot> Identities { get; init; } = [];
}

internal sealed class RgfAuthenticationIdentitySnapshot
{
    public string? AuthenticationType { get; init; }

    public string? NameClaimType { get; init; }

    public string? RoleClaimType { get; init; }

    public IReadOnlyList<RgfAuthenticationClaimSnapshot> Claims { get; init; } = [];
}

internal sealed class RgfAuthenticationClaimSnapshot
{
    public string Type { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string? ValueType { get; init; }

    public string? Issuer { get; init; }

    public string? OriginalIssuer { get; init; }
}

internal sealed class RgfAuthenticationPrincipalFactory(IOptions<RgfAuthenticationOptions> options)
{
    private static readonly string[] DefaultNameClaimFallbackTypes =
    [
        "name",
        "preferred_username",
        ClaimTypes.Name,
        ClaimTypes.Email,
        "email",
        ClaimTypes.Upn,
        "upn",
        "sub"
    ];

    public ClaimsPrincipal Create(RgfAuthenticationPrincipalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var identities = snapshot.Identities.Select(CreateIdentity).ToArray();
        return new ClaimsPrincipal(identities);
    }

    private ClaimsIdentity CreateIdentity(RgfAuthenticationIdentitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var nameClaimType = GetClaimTypeOrDefault(snapshot.NameClaimType, ClaimsIdentity.DefaultNameClaimType);
        var roleClaimType = GetClaimTypeOrDefault(snapshot.RoleClaimType, ClaimsIdentity.DefaultRoleClaimType);
        var claims = snapshot.Claims.Select(CreateClaim).ToList();

        EnsureNameClaim(claims, nameClaimType);

        return new ClaimsIdentity(
            claims,
            snapshot.AuthenticationType,
            nameClaimType,
            roleClaimType);
    }

    private static Claim CreateClaim(RgfAuthenticationClaimSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Claim(
            snapshot.Type,
            snapshot.Value,
            string.IsNullOrWhiteSpace(snapshot.ValueType) ? ClaimValueTypes.String : snapshot.ValueType,
            string.IsNullOrWhiteSpace(snapshot.Issuer) ? ClaimsIdentity.DefaultIssuer : snapshot.Issuer,
            string.IsNullOrWhiteSpace(snapshot.OriginalIssuer) ? ClaimsIdentity.DefaultIssuer : snapshot.OriginalIssuer);
    }

    private static string GetClaimTypeOrDefault(string? claimType, string fallback)
    {
        return string.IsNullOrWhiteSpace(claimType) ? fallback : claimType;
    }

    private void EnsureNameClaim(ICollection<Claim> claims, string nameClaimType)
    {
        if (claims.Any(claim => string.Equals(claim.Type, nameClaimType, StringComparison.Ordinal)))
        {
            return;
        }

        var sourceClaim = GetEffectiveNameClaimFallbackTypes()
            .Select(fallbackType => claims.FirstOrDefault(claim => string.Equals(claim.Type, fallbackType, StringComparison.Ordinal)))
            .FirstOrDefault(claim => claim is not null);

        if (sourceClaim is null || string.IsNullOrWhiteSpace(sourceClaim.Value))
        {
            return;
        }

        claims.Add(new Claim(
            nameClaimType,
            sourceClaim.Value,
            sourceClaim.ValueType,
            sourceClaim.Issuer,
            sourceClaim.OriginalIssuer));
    }

    private IReadOnlyList<string> GetEffectiveNameClaimFallbackTypes()
    {
        return options.Value.NameClaimFallbackTypes ?? DefaultNameClaimFallbackTypes;
    }
}
