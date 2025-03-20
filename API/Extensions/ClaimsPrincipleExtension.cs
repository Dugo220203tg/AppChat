using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims; 
namespace API.Extensions;

public static class ClaimsPrincipleExtension
{
    public static string GetUserName (this ClaimsPrincipal user)
    {
        return user.FindFirstValue (ClaimTypes.Name) ?? 
        throw new Exception("Cannot get username");
    }

    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}


