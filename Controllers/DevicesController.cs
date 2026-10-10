using ConnectBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly IBigQueryService _bq;

    public DevicesController(IBigQueryService bq) => _bq = bq;

    /// <summary>Register / refresh a user's device details and FCM token.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] DeviceInfoDto dto)
    {
        if (dto.UserId <= 0 || string.IsNullOrWhiteSpace(dto.DeviceId))
            return BadRequest(new { success = false, message = "userId and deviceId are required." });

        await _bq.RegisterDeviceAsync(dto);
        return Ok(new { success = true });
    }
}
