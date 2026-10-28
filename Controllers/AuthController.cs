using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HealthCare.Models;
using HealthCare.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace HealthCare.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(ClinicDb db, IPasswordHasher<Staff> hasher, IAntiforgery antiforgery, IWebHostEnvironment env, IConfiguration config, ClinicClock clock) : ControllerBase
{
    [HttpGet("token")]
    public object Token() => new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };
    [HttpGet("config")]
    public object Config() => new { name = config["Clinic:Name"], demo = env.IsDevelopment() && config.GetValue<bool>("Clinic:SeedDemo"), today = clock.Today.ToString("yyyy-MM-dd"), timeZone = clock.Zone.Id, feeCents = config.GetValue<int>("Clinic:ConsultationFeeCents") };
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await db.Staff.SingleOrDefaultAsync(x => x.Username == request.Username.Trim().ToLower());
        if (user is null || user.LockedUntilUtc > DateTime.UtcNow) return Unauthorized(new { message = "账号或密码错误，或账号暂时锁定。" });
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            var lockUntil = DateTime.UtcNow.AddMinutes(15);
            await db.Staff.Where(s => s.Id == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LockedUntilUtc, s => s.FailedAttempts >= 4 ? lockUntil : s.LockedUntilUtc).SetProperty(s => s.FailedAttempts, s => s.FailedAttempts >= 4 ? 0 : s.FailedAttempts + 1));
            return Unauthorized(new { message = "账号或密码错误，连续失败将锁定 15 分钟。" });
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.FailedAttempts = 0; user.LockedUntilUtc = null;
        db.Audit.Add(new AuditEntry { Actor = user.Name, Action = "登录系统", Target = $"Staff/{user.Id}" });
        await db.SaveChangesAsync();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role), new Claim("credentialVersion", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))) }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Ok(new { user.Id, user.Name, user.Role, user.Department });
    }
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await db.Staff.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return user is null ? Unauthorized() : Ok(new { user.Id, user.Name, user.Role, user.Department });
    }
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return Ok(new { message = "已退出" }); }
    [Authorize, HttpPost("password")]
    public async Task<IActionResult> Password(PasswordRequest request)
    {
        var user = (await db.Staff.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)))!;
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed) return BadRequest(new { message = "当前密码不正确。" });
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        db.Audit.Add(new AuditEntry { Actor = user.Name, Action = "修改密码", Target = $"Staff/{user.Id}" });
        await db.SaveChangesAsync(); await HttpContext.SignOutAsync(); return Ok(new { message = "密码已更新，请重新登录。" });
    }
}
