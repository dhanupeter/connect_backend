using System.Text;
using ConnectBackend.Data;
using ConnectBackend.Repositories;
using ConnectBackend.Repositories.Interfaces;
using ConnectBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers
builder.Services.AddControllers();

// 2. Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ConnectEV_SuperSecretSecurityKey_2027_PowerUpAnywhereAnytime!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ConnectEVBackend";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ConnectEVMobileApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// 3. Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. Add CORS policy for Flutter mobile, web, and emulator clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 5. Register Data Layer, Repositories, and Services
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IStationRepository, StationRepository>();
builder.Services.AddScoped<IMasterDataRepository, MasterDataRepository>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IBigQueryService, BigQueryService>();
builder.Services.AddSingleton<IPushService, PushService>();

builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITurnCredentialsService, CloudflareTurnService>();
builder.Services.AddSingleton<IActivityLogger, ActivityLogger>();

var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles();

// 6. Enable Swagger & Swagger UI middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Connect EV API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Connect EV - Enterprise API & Swagger Documentation";
});

// Root Redirect to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ConnectBackend.Hubs.ChatHub>("/hubs/chat");

// Admin endpoint to upload/update FCM secret in BigQuery
app.MapPost("/api/admin/store-fcm-secret", async (IPushService pushService, IBigQueryService bq) =>
{
    var fcmPath = File.Exists("fcm-key.json") ? "fcm-key.json" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "projecct1-434f1-firebase-adminsdk-1vnr3-c41a4f654e.json");
    if (!File.Exists(fcmPath)) return Results.NotFound(new { message = "FCM key file not found on disk." });
    var rawJson = await File.ReadAllTextAsync(fcmPath);
    var ok = await pushService.StoreFcmKeyInBigQueryAsync(rawJson);
    return ok 
        ? Results.Ok(new { message = "FCM key encrypted and stored in BigQuery successfully." }) 
        : Results.Problem("Failed to store FCM secret.");
});

// Admin endpoint to verify encrypted secret in BigQuery
app.MapGet("/api/admin/fcm-secret-status", async (IBigQueryService bq, IConfiguration config) =>
{
    var encrypted = await bq.GetSecretAsync("fcm_service_account");
    if (string.IsNullOrEmpty(encrypted)) return Results.Ok(new { configured = false, message = "Secret not found in BigQuery." });
    var key = config["Security:SecretEncryptionKey"] ?? SecretEncryption.FallbackKey;
    var decrypted = SecretEncryption.Decrypt(encrypted, key);
    var hasProjectId = decrypted.Contains("project_id");
    return Results.Ok(new 
    { 
        configured = true, 
        encryptedLength = encrypted.Length, 
        algorithm = SecretEncryption.DefaultAlgorithm, 
        decryptedValid = hasProjectId 
    });
});

// 7. CLI command handler to sync secrets immediately
if (args.Contains("--sync-secrets"))
{
    var bq = app.Services.GetRequiredService<IBigQueryService>() as BigQueryService;
    var push = app.Services.GetRequiredService<IPushService>();
    if (bq != null)
    {
        Console.WriteLine("[CLI] Ensuring BigQuery app_secrets table...");
        await bq.EnsureSecretsTableAsync();
        var fcmPath = File.Exists("fcm-key.json") ? "fcm-key.json" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "projecct1-434f1-firebase-adminsdk-1vnr3-c41a4f654e.json");
        if (File.Exists(fcmPath))
        {
            var raw = await File.ReadAllTextAsync(fcmPath);
            var ok = await push.StoreFcmKeyInBigQueryAsync(raw);
            Console.WriteLine($"[CLI] FCM Secret Encrypted & Stored in BigQuery: {ok}");
            var retrieved = await bq.GetSecretAsync("fcm_service_account");
            Console.WriteLine($"[CLI] Verified encrypted secret in BigQuery, length: {retrieved?.Length ?? 0} characters.");
        }
        else
        {
            Console.WriteLine("[CLI] Error: fcm-key.json not found.");
        }
    }
    return;
}

if (args.Contains("--test-push"))
{
    var bq = app.Services.GetRequiredService<IBigQueryService>() as BigQueryService;
    var push = app.Services.GetRequiredService<IPushService>();
    if (bq != null)
    {
        var sql = $"SELECT user_id, device_id, fcm_token, platform, manufacturer, model, last_seen_at FROM `{bq.ProjectId}.{bq.DatasetId}.user_devices` ORDER BY last_seen_at DESC LIMIT 10";
        var rows = await bq.ExecuteQueryAsync(sql);
        if (rows == null || !rows.Any())
        {
            Console.WriteLine("[CLI] No devices found in user_devices table!");
        }
        else
        {
            Console.WriteLine($"[CLI] Found {rows.Count()} device records:");
            foreach (var r in rows)
            {
                var uid = r["user_id"]?.ToString();
                var tok = r["fcm_token"]?.ToString();
                var mod = r["model"]?.ToString();
                var plat = r["platform"]?.ToString();
                var seen = r["last_seen_at"]?.ToString();
                Console.WriteLine($" - User {uid} | Model: {mod} ({plat}) | Seen: {seen}");
                Console.WriteLine($"   Token: {(string.IsNullOrEmpty(tok) ? "(EMPTY)" : tok[..Math.Min(tok.Length, 35)] + "...")}");
                if (!string.IsNullOrEmpty(uid) && long.TryParse(uid, out var userId))
                {
                    Console.WriteLine($"   Sending test push notification to user {userId}...");
                    await push.SendToUserAsync(userId, "Connect EV Notification", "Incoming call / message notification is active!", new Dictionary<string, string> { ["type"] = "test" }, true);
                    Console.WriteLine("   Push dispatch completed.");
                }
            }
        }
    }
    return;
}

// 8. Ensure BigQuery activity_logs and app_secrets tables exist on startup
var bqService = app.Services.GetRequiredService<IBigQueryService>() as BigQueryService;
if (bqService != null)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await bqService.EnsureActivityLogsTableAsync();
            await bqService.EnsureSecretsTableAsync();

            var existingSecret = await bqService.GetSecretAsync("fcm_service_account");
            if (string.IsNullOrEmpty(existingSecret))
            {
                var fcmPath = File.Exists("fcm-key.json") ? "fcm-key.json" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "projecct1-434f1-firebase-adminsdk-1vnr3-c41a4f654e.json");
                if (File.Exists(fcmPath))
                {
                    var rawKey = await File.ReadAllTextAsync(fcmPath);
                    var pushService = app.Services.GetRequiredService<IPushService>();
                    await pushService.StoreFcmKeyInBigQueryAsync(rawKey);
                    app.Logger.LogInformation("FCM key automatically encrypted and uploaded to BigQuery app_secrets on startup!");
                }
            }

            var actLogger = app.Services.GetRequiredService<IActivityLogger>();
            await actLogger.LogSuccessAsync("SystemStartup", "System", "Connect EV Backend service started with BigQuery activity logging and encrypted secrets active.");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning("Startup BigQuery table ensure warning: {Msg}", ex.Message);
        }
    });
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");
