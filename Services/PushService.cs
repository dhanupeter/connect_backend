using ConnectBackend.Models;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace ConnectBackend.Services;

public class DeviceInfoDto
{
    public long UserId { get; set; }
    public string? DeviceId { get; set; }
    public string? FcmToken { get; set; }
    public string? Platform { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
}

public interface IPushService
{
    bool IsEnabled { get; }
    Task SendToUserAsync(long userId, string title, string body, Dictionary<string, string>? data = null, bool highPriority = false);
}

/// <summary>
/// Sends Firebase Cloud Messaging pushes. Needs a service account for the Firebase project
/// (env FCM_SERVICE_ACCOUNT_KEY with the JSON, or file fcm-key.json). Without it, pushes are skipped.
/// </summary>
public class PushService : IPushService
{
    private readonly IBigQueryService _bq;
    private readonly ILogger<PushService> _logger;
    private readonly FirebaseMessaging? _messaging;
    public bool IsEnabled => _messaging != null;

    public PushService(IBigQueryService bq, IConfiguration config, ILogger<PushService> logger)
    {
        _bq = bq;
        _logger = logger;
        try
        {
            var json = Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_KEY");
            GoogleCredential? cred = null;
            if (!string.IsNullOrWhiteSpace(json)) cred = GoogleCredential.FromJson(json);
            else if (File.Exists("fcm-key.json")) cred = GoogleCredential.FromFile("fcm-key.json");

            if (cred == null)
            {
                _logger.LogWarning("FCM not configured (set FCM_SERVICE_ACCOUNT_KEY). Push notifications disabled.");
                return;
            }

            var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
            _messaging = FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FCM.");
        }
    }

    public async Task SendToUserAsync(long userId, string title, string body, Dictionary<string, string>? data = null, bool highPriority = false)
    {
        if (_messaging == null) return;
        try
        {
            var tokens = (await _bq.GetDeviceTokensAsync(userId)).ToList();
            foreach (var token in tokens)
            {
                var msg = new Message
                {
                    Token = token,
                    Notification = new Notification { Title = title, Body = body },
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = highPriority ? Priority.High : Priority.Normal,
                        Notification = new AndroidNotification
                        {
                            ChannelId = highPriority ? "calls" : "chat",
                            Sound = "default"
                        }
                    }
                };
                try { await _messaging.SendAsync(msg); }
                catch (Exception ex) { _logger.LogWarning("FCM send failed for a token: {Msg}", ex.Message); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push send failed for user {UserId}", userId);
        }
    }
}
