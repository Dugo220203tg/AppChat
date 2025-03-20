using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace API.Services;

public class TokensService
{
    private readonly IConfiguration _config;

    public TokensService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(string userId, string userName)
    {
        // Get the security key
        var securityKey = _config["JWTSettings:SecurityKey"];
        if (string.IsNullOrEmpty(securityKey))
        {
            throw new InvalidOperationException("JWT Security Key is missing in configuration");
        }

        // Ensure key is properly sized
        var keyBytes = Encoding.UTF8.GetBytes(securityKey);
        if (keyBytes.Length < 32)
        {
            securityKey = securityKey.PadRight(32, '!');
            keyBytes = Encoding.UTF8.GetBytes(securityKey);
        }

        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName)
        };

        var token = new JwtSecurityToken(
            issuer: null,
            audience: null,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}