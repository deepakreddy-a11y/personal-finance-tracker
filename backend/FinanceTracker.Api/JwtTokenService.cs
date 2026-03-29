using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FinanceTracker.Api;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "FinanceTracker.Api";
    public string Audience { get; init; } = "FinanceTracker.Frontend";
    public string SigningKey { get; init; } = "finance-tracker-super-secret-key-change-before-production-2026";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;

    public TimeSpan AccessTokenTtl => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenTtl => TimeSpan.FromDays(RefreshTokenDays);
}

public sealed record AccessTokenIssue(string Token, DateTime ExpiresAtUtc);

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key must be at least 32 characters.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenIssue CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(_options.AccessTokenTtl);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _signingCredentials);

        return new AccessTokenIssue(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
