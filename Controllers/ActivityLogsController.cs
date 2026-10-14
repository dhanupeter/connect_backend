using ConnectBackend.Models;
using ConnectBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivityLogsController : ControllerBase
{
    private readonly IActivityLogger _activityLogger;

    public ActivityLogsController(IActivityLogger activityLogger)
    {
        _activityLogger = activityLogger;
    }

    /// <summary>
    /// Log an activity, error, warning, exception, or success event from client or server
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateLog([FromBody] CreateActivityLogDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Action))
        {
            return BadRequest(new { success = false, message = "Action is required." });
        }

        var log = await _activityLogger.LogAsync(dto);
        return Ok(new { success = true, data = log });
    }

    /// <summary>
    /// Query activity logs with filtering by status, category, action, userId, correlationId
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? status = null,
        [FromQuery] string? category = null,
        [FromQuery] string? action = null,
        [FromQuery] string? userId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] int limit = 100)
    {
        var logs = await _activityLogger.GetLogsAsync(status, category, action, userId, correlationId, limit);
        return Ok(new { success = true, count = logs.Count, data = logs });
    }

    /// <summary>
    /// Get statistical breakdown of logs by status (Success, Warning, Error, Exception)
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _activityLogger.GetStatsAsync();
        return Ok(new { success = true, stats });
    }

    /// <summary>
    /// Clear in-memory log buffer
    /// </summary>
    [HttpDelete("clear")]
    public IActionResult ClearLogs()
    {
        _activityLogger.ClearInMemoryLogs();
        return Ok(new { success = true, message = "In-memory activity logs cleared." });
    }
}
