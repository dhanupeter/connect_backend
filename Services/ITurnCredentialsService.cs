namespace ConnectBackend.Services;

public class TurnCredentialsResponse
{
    public bool Success { get; set; }
    public string Provider { get; set; } = "cloudflare";
    public string? Message { get; set; }
    public List<object> IceServers { get; set; } = new();
}

public interface ITurnCredentialsService
{
    bool IsConfigured { get; }
    Task<TurnCredentialsResponse> GetIceServersAsync(long? userId = null);
}
