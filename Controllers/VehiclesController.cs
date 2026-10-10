using ConnectBackend.Dtos;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehiclesController : ControllerBase
{
    private readonly IVehicleRepository _vehicleRepository;

    public VehiclesController(IVehicleRepository vehicleRepository)
    {
        _vehicleRepository = vehicleRepository;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetVehicle(string userId)
    {
        if (!long.TryParse(userId, out long uid))
        {
            return BadRequest(new { message = "Invalid userId format." });
        }

        var vehicle = await _vehicleRepository.GetByUserIdAsync(uid);
        if (vehicle == null)
        {
            return NotFound(new { message = "No vehicle found for user." });
        }

        string displayModel = !string.IsNullOrEmpty(vehicle.ModelName) 
            ? $"{vehicle.ManufacturerName} {vehicle.ModelName}" 
            : "Tata Nexon.ev";

        return Ok(new VehicleDto(
            vehicle.RegistrationNumber,
            displayModel,
            (double)(vehicle.BatteryCapacityKwh ?? 45.0m),
            "CCS2 (DC Fast)",
            (int)(vehicle.RangeKm ?? 465.0m),
            true
        ));
    }

    [HttpPost]
    [HttpPost("/api/user/vehicle")]
    public async Task<IActionResult> UpdateVehicle([FromBody] UpdateVehicleRequest request)
    {
        if (!long.TryParse(request.UserId, out long uid))
        {
            return BadRequest(new { message = "Invalid userId format." });
        }

        var vehicle = new Vehicle
        {
            UserId = uid,
            RegistrationNumber = request.Plate,
            Color = "Teal Blue",
            IsElectric = true,
            Status = "ACTIVE"
        };

        var saved = await _vehicleRepository.UpsertVehicleAsync(vehicle);
        return Ok(new { success = true, message = "Vehicle updated in database", vehicleId = saved.VehicleId });
    }

    [HttpGet("{vehicleId}/telemetry")]
    public async Task<IActionResult> GetTelemetry(long vehicleId)
    {
        var telemetry = await _vehicleRepository.GetLatestTelemetryAsync(vehicleId);
        if (telemetry == null)
        {
            return NotFound(new { message = "No telemetry recorded for this vehicle." });
        }

        return Ok(new TelemetryDto(
            telemetry.VehicleId,
            telemetry.SOC,
            telemetry.SpeedKmh,
            telemetry.RangeKm,
            telemetry.BatteryTemperature,
            telemetry.ChargingPowerKw,
            telemetry.RecordedAt
        ));
    }
}
