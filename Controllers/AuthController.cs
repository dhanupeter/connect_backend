using ConnectBackend.Dtos;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using ConnectBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IBigQueryService _bqService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        IVehicleRepository vehicleRepository,
        IJwtTokenService jwtTokenService,
        IBigQueryService bqService,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _vehicleRepository = vehicleRepository;
        _jwtTokenService = jwtTokenService;
        _bqService = bqService;
        _logger = logger;
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required." });
        }

        string uid = !string.IsNullOrWhiteSpace(request.FirebaseUid)
            ? request.FirebaseUid
            : "firebase-" + Guid.NewGuid().ToString("N")[..12];

        string email = request.Email.Trim().ToLowerInvariant();
        string fullName = request.DisplayName ?? "EV Driver";

        // Parse first & last name
        var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string firstName = nameParts.Length > 0 ? nameParts[0] : (string.IsNullOrWhiteSpace(fullName) ? "EV Driver" : fullName);
        string lastName = nameParts.Length > 1 ? nameParts[1] : "";

        var userToSave = new User
        {
            FirebaseUid = uid,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            ProfileImageUrl = request.PhotoUrl,
            MobileNumber = "+91 98765 43210",
            Status = "ACTIVE",
            IsEmailVerified = true
        };

        var savedUser = await _userRepository.UpsertUserAsync(userToSave);

        // Fetch or create default vehicle for user
        var vehicle = await _vehicleRepository.GetByUserIdAsync(savedUser.UserId);
        if (vehicle == null)
        {
            vehicle = new Vehicle
            {
                UserId = savedUser.UserId,
                RegistrationNumber = savedUser.UserId == 1 ? "TN 38 EV 8899" : $"TN 38 EV {8899 + savedUser.UserId}",
                VIN = "MAT" + Guid.NewGuid().ToString("N")[..14].ToUpper(),
                Color = "Teal Blue",
                IsElectric = true,
                Status = "ACTIVE"
            };
            vehicle = await _vehicleRepository.UpsertVehicleAsync(vehicle);
        }

        // Persist user profile & vehicle directly to BigQuery connectdb synchronously
        try
        {
            await _bqService.InsertOrUpdateUserAsync(savedUser);
            if (vehicle != null)
            {
                await _bqService.InsertOrUpdateVehicleAsync(vehicle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigQuery sync error for user {UserId}", savedUser.UserId);
        }

        string jwtToken = _jwtTokenService.GenerateToken(savedUser);

        string displayModel = !string.IsNullOrEmpty(vehicle.ModelName) 
            ? $"{vehicle.ManufacturerName} {vehicle.ModelName}" 
            : "Tata Nexon.ev";

        var response = new AuthResponseDto(
            Success: true,
            Token: jwtToken,
            User: new UserDto(
                savedUser.UserId.ToString(),
                savedUser.FirebaseUid,
                savedUser.Email ?? "",
                $"{savedUser.FirstName} {savedUser.LastName}".Trim(),
                savedUser.MobileNumber,
                "EV_OWNER",
                savedUser.ProfileImageUrl,
                savedUser.UserId
            ),
            Vehicle: new VehicleDto(
                vehicle.RegistrationNumber,
                displayModel,
                (double)(vehicle.BatteryCapacityKwh ?? 45.0m),
                "CCS2 (DC Fast)",
                (int)(vehicle.RangeKm ?? 465.0m),
                true
            )
        );

        _logger.LogInformation("User {Email} logged in successfully with UserId: {UserId}", email, savedUser.UserId);
        return Ok(response);
    }
}
