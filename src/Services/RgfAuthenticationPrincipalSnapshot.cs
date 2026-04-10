using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Recrovit.RecroGridFramework.Client.Services;

internal sealed class RgfAuthenticationPrincipalSnapshot
{
    public bool IsAuthenticated { get; init; }

    public string? Name { get; init; }

    public string? PreferredUsername { get; init; }

    public string? Email { get; init; }

    public string? SubjectId { get; init; }

    public string? Issuer { get; init; }

    public string? ObjectId { get; init; }

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
    private const string MinimalSnapshotAuthenticationType = "server-proxy";
    private const string ObjectIdClaimType = "oid";
    private const string PreferredUsernameClaimType = "preferred_username";

    private static readonly string[] DefaultNameClaimFallbackTypes =
    [
        "name",
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

        if (snapshot.IsAuthenticated && snapshot.Identities.Count == 0)
        {
            return new ClaimsPrincipal(CreateMinimalIdentity(snapshot));
        }

        var identities = snapshot.Identities.Select(CreateIdentity).ToArray();
        return new ClaimsPrincipal(identities);
    }

    private ClaimsIdentity CreateMinimalIdentity(RgfAuthenticationPrincipalSnapshot snapshot)
    {
        var nameClaimType = ClaimsIdentity.DefaultNameClaimType;
        var roleClaimType = ClaimsIdentity.DefaultRoleClaimType;
        var claims = new List<Claim>();

        AddMinimalClaim(claims, nameClaimType, snapshot.Name, snapshot.Issuer);
        AddMinimalClaim(claims, PreferredUsernameClaimType, snapshot.PreferredUsername, snapshot.Issuer);
        AddMinimalClaim(claims, ClaimTypes.Email, snapshot.Email, snapshot.Issuer);
        AddMinimalClaim(claims, "email", snapshot.Email, snapshot.Issuer);
        AddMinimalClaim(claims, ClaimTypes.NameIdentifier, snapshot.SubjectId, snapshot.Issuer);
        AddMinimalClaim(claims, ObjectIdClaimType, snapshot.ObjectId, snapshot.Issuer);

        return new ClaimsIdentity(claims, MinimalSnapshotAuthenticationType, nameClaimType, roleClaimType);
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

    private static void AddMinimalClaim(ICollection<Claim> claims, string claimType, string? value, string? issuer)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        claims.Add(new Claim(
            claimType,
            value,
            ClaimValueTypes.String,
            string.IsNullOrWhiteSpace(issuer) ? ClaimsIdentity.DefaultIssuer : issuer,
            string.IsNullOrWhiteSpace(issuer) ? ClaimsIdentity.DefaultIssuer : issuer));
    }

    private IReadOnlyList<string> GetEffectiveNameClaimFallbackTypes()
    {
        return options.Value.NameClaimFallbackTypes ?? DefaultNameClaimFallbackTypes;
    }
}
