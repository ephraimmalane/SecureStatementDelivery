using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Authentication;

internal sealed class DownloadTokenService(IOptions<DownloadTokenOptions> options) : IDownloadTokenService
{
    private const string StatementIdClaim = "sid";

    public (string Token, Guid TokenId) GenerateToken(Guid statementId, Guid userId, DateTime expiresAt)
    {
        var tokenId = Guid.NewGuid();

        string secretKey = options.Value.Secret;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId.ToString()),
                new Claim(StatementIdClaim, statementId.ToString())
            ]),
            Expires = expiresAt,
            SigningCredentials = credentials,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience
        };

        var handler = new JsonWebTokenHandler();
        string token = handler.CreateToken(tokenDescriptor);

        return (token, tokenId);
    }

    public DownloadTokenClaims? ValidateToken(string token)
    {
        try
        {
            var handler = new JsonWebTokenHandler();

            TokenValidationResult result = handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                // Accept the current key plus any key still inside its rotation overlap window, so a
                // link signed before a key rotation keeps validating until its TTL elapses.
                IssuerSigningKeys = GetValidationKeys(),
                ValidIssuer = options.Value.Issuer,
                ValidAudience = options.Value.Audience,
                ClockSkew = TimeSpan.Zero
            }).GetAwaiter().GetResult();

            if (!result.IsValid)
            {
                return null;
            }

            string? userIdStr = result.Claims[JwtRegisteredClaimNames.Sub]?.ToString();
            string? tokenIdStr = result.Claims[JwtRegisteredClaimNames.Jti]?.ToString();
            string? statementIdStr = result.Claims[StatementIdClaim]?.ToString();

            if (!Guid.TryParse(userIdStr, out Guid userId) ||
                !Guid.TryParse(tokenIdStr, out Guid tokenId) ||
                !Guid.TryParse(statementIdStr, out Guid statementId))
            {
                return null;
            }

            DateTime expiresAt = result.SecurityToken.ValidTo;

            return new DownloadTokenClaims(tokenId, statementId, userId, expiresAt);
        }
        catch
        {
            return null;
        }
    }

    public string HashToken(string rawToken)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    // Current signing key first, then any previous keys still in their overlap window. HS256 tokens
    // carry no `kid`, so the handler tries every key until one verifies the signature.
    private IEnumerable<SecurityKey> GetValidationKeys()
    {
        yield return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.Secret));

        foreach (string previous in options.Value.PreviousSecrets)
        {
            if (!string.IsNullOrWhiteSpace(previous))
            {
                yield return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(previous));
            }
        }
    }
}
