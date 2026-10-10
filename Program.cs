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

// Real-time WebSockets & SignalR
builder.Services.AddSignalR();

var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles();

// 6. Enable Swagger & Swagger UI middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Connect EV API v1");
    c.RoutePrefix = "swagger"; // Accessible at http://localhost:5000/swagger
    c.DocumentTitle = "Connect EV - Enterprise API & Swagger Documentation";
});

// Root Redirect to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ConnectBackend.Hubs.ChatHub>("/hubs/chat");

app.Run("http://0.0.0.0:5000");
