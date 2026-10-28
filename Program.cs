using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HealthCare.Models;
using HealthCare.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDirectory = Path.GetFullPath(builder.Configuration["Clinic:DataDirectory"] ?? "App_Data");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("QingHeClinic");
builder.Services.AddDbContext<ClinicDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataDirectory, "clinic.db")};Foreign Keys=True;Default Timeout=10"));
builder.Services.AddSingleton<ClinicClock>();
builder.Services.AddScoped<IPasswordHasher<Staff>, PasswordHasher<Staff>>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = c => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = "请检查填写内容。", errors = c.ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage)) }));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "QingHe.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var id = c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = c.HttpContext.RequestServices.GetRequiredService<ClinicDb>();
        var user = int.TryParse(id, out var staffId) ? await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == staffId) : null;
        if (user is null || c.Principal?.FindFirstValue("credentialVersion") != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))) c.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
var app = builder.Build();
if (builder.Configuration.GetValue<int>("Clinic:ConsultationFeeCents") is <= 0 or > 1000000) throw new InvalidOperationException("诊疗费配置必须为 1–1000000 分。");
await Seeder.Initialize(app.Services, app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Clinic:SeedDemo"), app.Configuration);
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (c, next) =>
{
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["X-Frame-Options"] = "DENY";
    c.Response.Headers["Referrer-Policy"] = "same-origin";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (c.Request.Path.StartsWithSegments("/api")) c.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (DbUpdateConcurrencyException) { c.Response.StatusCode = 409; await c.Response.WriteAsJsonAsync(new { message = "记录已被其他操作更新，请刷新后重试。" }); }
    catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { c.Response.StatusCode = 409; await c.Response.WriteAsJsonAsync(new { message = "数据冲突：该时段已预约，或记录已存在。" }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Request failed {Trace}", c.TraceIdentifier); c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(new { message = "操作失败，请稍后重试。", traceId = c.TraceIdentifier }); }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (c, next) =>
{
    if (c.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(c.Request.Method) && !HttpMethods.IsHead(c.Request.Method))
    {
        try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); }
        catch (AntiforgeryValidationException) { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(new { message = "安全令牌已过期，请刷新页面。" }); return; }
    }
    await next();
});
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
