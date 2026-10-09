using System.Text.Json.Serialization;
using Chitra.Api.Hubs;
using Chitra.Api.Infrastructure;
using Chitra.Application;
using Chitra.Application.Abstractions;
using Chitra.Application.Settings;
using Chitra.Infrastructure;
using Chitra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.Configure<HostInfoOptions>(o => o.AuthMode = authOptions.Enabled ? "Jwt" : "None");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services
    .AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 220L * 1024 * 1024);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

if (authOptions.Enabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.Authority = authOptions.Authority;
            o.Audience = authOptions.Audience;
            o.Events = new JwtBearerEvents
            {
                // Browsers cannot set headers on <img>/<video> or WebSocket requests, so allow ?access_token= there.
                OnMessageReceived = context =>
                {
                    var path = context.HttpContext.Request.Path;
                    if (context.Request.Query.TryGetValue("access_token", out var token) &&
                        (path.StartsWithSegments("/hubs") || path.StartsWithSegments("/api/media")))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                },
            };
        });
    builder.Services.AddAuthorization();
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<SettingsService>().LoadAsync(CancellationToken.None);
}

app.UseExceptionHandler();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<UserProvisioningMiddleware>();
}

// Serve a production Angular build from wwwroot when present (single-process deployment).
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapControllers();
var hub = app.MapHub<StudioHub>(StudioHub.Path);
if (authOptions.Enabled)
{
    api.RequireAuthorization();
    hub.RequireAuthorization();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
