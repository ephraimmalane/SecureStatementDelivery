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
            string secretKey = options.Value.Secret;
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

            var handler = new JsonWebTokenHandler();

            TokenValidationResult result = handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                IssuerSigningKey = securityKey,
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
}
