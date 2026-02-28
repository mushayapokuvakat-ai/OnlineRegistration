using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OnlineRegistrationSystem.Data;
using OnlineRegistrationSystem.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// ─── Services Registration ───────────────────────────────────────
builder.Services.AddControllers();

// Register custom services
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<CourseRepository>();
builder.Services.AddSingleton<EnrollmentRepository>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<EmailService>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret must be configured in appsettings.json");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

builder.Services.AddAuthorization();

// CORS — allow the frontend to call the API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure Kestrel to remove the 'Server' header for better security (Stealth Mode)
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// Configure Forwarded Headers to see real client IP (Fix for Ngrok/Proxies)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clearing known networks/proxies because we don't know Ngrok's IP ranges
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate Limiting — Basic firewall against DoS attacks
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
        opt.QueueLimit = 0;
    });

    // Strategy: Limit by REAL IP address (using X-Forwarded-For if available)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault() 
                        ?? httpContext.Connection.RemoteIpAddress?.ToString() 
                        ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ipAddress,
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

var app = builder.Build();

// ─── Middleware Pipeline ──────────────────────────────────────────

// Global error handling middleware
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            "{\"success\":false,\"message\":\"An unexpected error occurred.\"}");
    });
});

// Use Forwarded Headers to detect real client IP
app.UseForwardedHeaders();

app.UseRateLimiter();
app.UseCors("AllowAll");

// Serve static files from wwwroot (our HTML/JS/CSS frontend)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ─── Database Initialization ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<UserRepository>();
    await repo.EnsureDatabaseExistsAsync();
    await repo.EnsureTableExistsAsync();
}

// ─── Security Headers & HTTPS Enforcement ────────────────────────────
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}

app.UseHttpsRedirection();

// --- Ultimate Bot Filter & Security Headers ---
app.Use(async (context, next) =>
{
    // 1. Aggressive Bot/Script Blocking
    var userAgent = context.Request.Headers["User-Agent"].ToString().ToLower();
    var illegalBots = new[] { "curl", "python", "postman", "go-http", "java", "wget", "httpclient" };
    
    if (illegalBots.Any(bot => userAgent.Contains(bot)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Automated access is blocked. Please use a standard web browser.");
        return;
    }

    // 2. Harden Browser Shields
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    
    // Strict HSTS (1 year + subdomains)
    context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");

    // Tight Content Security Policy (CSP)
    context.Response.Headers.Append("Content-Security-Policy", 
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "connect-src 'self';");

    await next();
});

app.Run();
