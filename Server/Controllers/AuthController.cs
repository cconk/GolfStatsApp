using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        public const string AdminRole = "Admin";

        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly RoleManager<IdentityRole> _roles;
        private readonly IConfiguration _config;

        public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn,
            RoleManager<IdentityRole> roles, IConfiguration config)
        {
            _users = users;
            _signIn = signIn;
            _roles = roles;
            _config = config;
        }

        [HttpGet("me")]
        public async Task<UserInfo> Me()
        {
            if (User.Identity?.IsAuthenticated != true) return new UserInfo();
            var user = await _users.GetUserAsync(User);
            if (user is null) return new UserInfo();
            return await ToInfo(user);
        }

        [HttpPost("register")]
        public async Task<ActionResult<UserInfo>> Register(AuthRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");

            var user = new AppUser
            {
                UserName = request.Email.Trim(),
                Email = request.Email.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? request.Email.Split('@')[0]
                    : request.DisplayName.Trim()
            };

            var result = await _users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

            // The first account, or any email listed in Admin:Emails, becomes an administrator.
            var adminEmails = _config.GetSection("Admin:Emails").Get<string[]>() ?? Array.Empty<string>();
            var isFirst = await _users.Users.CountAsync() == 1;
            if (isFirst || adminEmails.Contains(user.Email, StringComparer.OrdinalIgnoreCase))
            {
                if (!await _roles.RoleExistsAsync(AdminRole))
                    await _roles.CreateAsync(new IdentityRole(AdminRole));
                await _users.AddToRoleAsync(user, AdminRole);
            }

            await _signIn.SignInAsync(user, isPersistent: true);
            return await ToInfo(user);
        }

        [HttpPost("login")]
        public async Task<ActionResult<UserInfo>> Login(AuthRequest request)
        {
            var user = await _users.FindByEmailAsync(request.Email ?? string.Empty);
            if (user is null) return Unauthorized("Invalid email or password.");

            var result = await _signIn.PasswordSignInAsync(user, request.Password ?? string.Empty,
                isPersistent: true, lockoutOnFailure: true);
            if (result.IsLockedOut) return StatusCode(423, "Account locked. Try again later.");
            if (!result.Succeeded) return Unauthorized("Invalid email or password.");
            return await ToInfo(user);
        }

        [HttpPost("logout"), Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signIn.SignOutAsync();
            return NoContent();
        }

        private async Task<UserInfo> ToInfo(AppUser user) => new()
        {
            IsAuthenticated = true,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsAdmin = await _users.IsInRoleAsync(user, AdminRole)
        };
    }
}
