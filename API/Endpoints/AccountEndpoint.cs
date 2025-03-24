using API.Common;
using API.DTOs;
using API.Extensions;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Endpoints
{
    public static class AccountEndpoint
    {
        public static RouteGroupBuilder MapAccountEndpoint(this WebApplication app)
        {
            var group = app.MapGroup("/api/account").WithTags("account");

            group.MapPost("/register", async (HttpContext context, UserManager<AppUser> userManager,
                [FromForm] string fullName, [FromForm] string email, [FromForm] string password,
                [FromForm] string userName, [FromForm] IFormFile? profileImage) =>
            {
                var userFromDb = await userManager.FindByEmailAsync(email);
                if (userFromDb is not null)
                {
                    return Results.BadRequest(Response<string>.Failure("User already exists."));
                }

                string picture = string.Empty;

                if (profileImage is not null)
                {
                    picture = await FileUpload.Upload(profileImage);
                    picture = $"{context.Request.Scheme}://{context.Request.Host}/uploads/{picture}";
                }
                else
                {
                    picture = $"{context.Request.Scheme}://{context.Request.Host}/uploads/default_avatar.jpg";
                }

                var user = new AppUser
                {
                    Email = email,
                    FullName = fullName,
                    UserName = userName,
                    ProfilePicture = picture
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    return Results.BadRequest(Response<string>.Failure(result.Errors.Select(x => x.Description).FirstOrDefault()!));
                }

                return Results.Ok(Response<string>.Success("", "User created successfully."));
            })
            .DisableAntiforgery();
            group.MapPost("/login", async (UserManager<AppUser> UserManager, TokensService tokensService, LoginDto dto) =>
            {
                if(dto is null){
                    return Results.BadRequest(Response<string>.Failure("Invalid login details."));
                }
                var user = await UserManager.FindByEmailAsync(dto.Email);
                if (user is null)
                {
                    return Results.BadRequest(Response<string>.Failure("User not found."));
                }
                var result = await UserManager.CheckPasswordAsync(user, dto.Password);
                if (!result)
                {
                    return Results.BadRequest(Response<string>.Failure("Invalid password."));
                }
                var token = tokensService.GenerateToken(user.Id, user.UserName! );
                return Results.Ok(Response<string>.Success(token, "Login successfully"));
            });
            
           group.MapGet("/me", async (HttpContext context, UserManager<AppUser> userManager, ILogger<Program> logger) =>
            {
                try
                {
                    var currentLoggedInUserId = context.User.GetUserId();

                    if (currentLoggedInUserId == null)
                    {
                        return Results.Unauthorized();
                    }
                    logger.LogInformation($"Retrieved user ID: {currentLoggedInUserId}");

                    var currentLoggedInUser = await userManager.Users.SingleOrDefaultAsync(x => x.Id == currentLoggedInUserId);
                    if (currentLoggedInUser == null)
                    {
                        logger.LogWarning("User not found in database");
                        return Results.NotFound(Response<string>.Failure("User not found"));
                    }
        
                    return Results.Ok(Response<AppUser>.Success(currentLoggedInUser, "User fetched successfully"));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in /me endpoint");
                    return Results.BadRequest(Response<string>.Failure(ex.Message));
                }
            }).RequireAuthorization();
            
            return group;
        }
    }
}