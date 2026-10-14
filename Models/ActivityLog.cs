namespace ConnectBackend.Models;

public class ActivityLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = "System"; // Chat, Call, Auth, Permission, System
    public string Status { get; set; } = "Success"; // Success, Warning, Error, Exception
    public string Message { get; set; } = string.Empty;
    public string? ExceptionDetails { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CreateActivityLogDto
{
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = "System";
    public string Status { get; set; } = "Success"; // Success, Warning, Error, Exception
    public string Message { get; set; } = string.Empty;
    public string? ExceptionDetails { get; set; }
    public string? CorrelationId { get; set; }
}
