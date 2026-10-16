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
    Task<bool> StoreFcmKeyInBigQueryAsync(string rawJson);
    Task SendToUserAsync(long userId, string title, string body, Dictionary<string, string>? data = null, bool highPriority = false);
}

/// <summary>
/// Sends Firebase Cloud Messaging pushes. Loads credentials securely from BigQuery app_secrets (encrypted)
/// with fallback to local fcm-key.json or FCM_SERVICE_ACCOUNT_KEY environment variable.
/// </summary>
public class PushService : IPushService
{
    private readonly IBigQueryService _bq;
    private readonly IConfiguration _config;
    private readonly ILogger<PushService> _logger;
    private readonly string _encryptionKey;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private FirebaseMessaging? _messaging;

    public bool IsEnabled => _messaging != null;

    public PushService(IBigQueryService bq, IConfiguration config, ILogger<PushService> logger)
    {
        _bq = bq;
        _config = config;
        _logger = logger;
        _encryptionKey = _config["Security:SecretEncryptionKey"] 
            ?? Environment.GetEnvironmentVariable("SECRET_ENCRYPTION_KEY") 
            ?? SecretEncryption.FallbackKey;

        // Kick off eager initialization in background
        _ = Task.Run(async () =>
        {
            try { await EnsureMessagingAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Background FCM init failed."); }
        });
    }

    public async Task<bool> StoreFcmKeyInBigQueryAsync(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return false;
        try
        {
            var encrypted = SecretEncryption.Encrypt(rawJson, _encryptionKey);
            await _bq.SetSecretAsync("fcm_service_account", encrypted, SecretEncryption.DefaultAlgorithm);
            _logger.LogInformation("FCM service account successfully encrypted and stored in BigQuery app_secrets.");

            // Update in-memory instance
            await _initLock.WaitAsync();
            try
            {
                var cred = GoogleCredential.FromJson(rawJson);
                var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
                _messaging = FirebaseMessaging.GetMessaging(app);
            }
            finally
            {
                _initLock.Release();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt and store FCM service account in BigQuery.");
            return false;
        }
    }

    private async ValueTask<FirebaseMessaging?> EnsureMessagingAsync()
    {
        if (_messaging != null) return _messaging;
        await _initLock.WaitAsync();
        try
        {
            if (_messaging != null) return _messaging;
            _messaging = await InitMessagingCoreAsync();
            return _messaging;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<FirebaseMessaging?> InitMessagingCoreAsync()
    {
        // 1. Try BigQuery encrypted secret
        try
        {
            var encrypted = await _bq.GetSecretAsync("fcm_service_account");
            if (!string.IsNullOrWhiteSpace(encrypted))
            {
                var decrypted = SecretEncryption.Decrypt(encrypted, _encryptionKey);
                if (!string.IsNullOrWhiteSpace(decrypted) && decrypted.Contains("project_id"))
                {
                    var cred = GoogleCredential.FromJson(decrypted);
                    var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
                    _logger.LogInformation("FCM successfully initialized from encrypted BigQuery secret.");
                    return FirebaseMessaging.GetMessaging(app);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load or decrypt FCM secret from BigQuery. Attempting fallbacks.");
        }

        // 2. Try environment variable
        var envJson = Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_KEY");
        if (!string.IsNullOrWhiteSpace(envJson))
        {
            try
            {
                var cred = GoogleCredential.FromJson(envJson);
                var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
                _logger.LogInformation("FCM initialized from FCM_SERVICE_ACCOUNT_KEY env variable.");
                return FirebaseMessaging.GetMessaging(app);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse FCM_SERVICE_ACCOUNT_KEY env variable.");
            }
        }

        // 3. Try local fcm-key.json
        if (File.Exists("fcm-key.json"))
        {
            try
            {
                var cred = GoogleCredential.FromFile("fcm-key.json");
                var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
                _logger.LogInformation("FCM initialized from local fcm-key.json file.");
                return FirebaseMessaging.GetMessaging(app);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load local fcm-key.json.");
            }
        }

        // 4. Try downloaded key from user profile
        var downloadsKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "projecct1-434f1-firebase-adminsdk-1vnr3-c41a4f654e.json");
        if (File.Exists(downloadsKey))
        {
            try
            {
                var cred = GoogleCredential.FromFile(downloadsKey);
                var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = cred });
                _logger.LogInformation("FCM initialized from user Downloads key.");
                return FirebaseMessaging.GetMessaging(app);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Downloads FCM key.");
            }
        }

        _logger.LogWarning("FCM not configured (no BigQuery secret or local key found). Push notifications disabled.");
        return null;
    }

    public async Task SendToUserAsync(long userId, string title, string body, Dictionary<string, string>? data = null, bool highPriority = false)
    {
        var messaging = await EnsureMessagingAsync();
        if (messaging == null) return;

        try
        {
            var tokens = (await _bq.GetDeviceTokensAsync(userId)).ToList();
            var payloadData = data != null ? new Dictionary<string, string>(data) : new Dictionary<string, string>();
            payloadData["title"] = title;
            payloadData["body"] = body;
            if (highPriority) payloadData["priority"] = "high";

            foreach (var token in tokens)
            {
                var msg = new Message
                {
                    Token = token,
                    Notification = new Notification { Title = title, Body = body },
                    Data = payloadData,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = highPriority ? "calls" : "chat",
                            Sound = "default",
                            Priority = highPriority ? NotificationPriority.MAX : NotificationPriority.HIGH,
                            DefaultSound = true,
                            DefaultVibrateTimings = true,
                            Visibility = NotificationVisibility.PUBLIC,
                            Icon = "ic_notification",
                            ClickAction = "FLUTTER_NOTIFICATION_CLICK"
                        }
                    }
                };
                try 
                { 
                    var response = await messaging.SendAsync(msg); 
                    _logger.LogInformation("FCM push successfully sent to user {UserId} (Token: {Tok}...): {Resp}", 
                        userId, token[..Math.Min(token.Length, 15)], response);
                }
                catch (FirebaseMessagingException fEx) when (fEx.MessagingErrorCode == MessagingErrorCode.Unregistered)
                {
                    _logger.LogWarning("FCM token unregistered for user {UserId}. Removing from BigQuery.", userId);
                    _ = _bq.ExecuteQueryAsync($"DELETE FROM `{_bq.ProjectId}.{_bq.DatasetId}.user_devices` WHERE fcm_token = '{token.Replace("'", "''")}'");
                }
                catch (Exception ex) { _logger.LogWarning("FCM send failed for token {Token}: {Msg}", token, ex.Message); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push send failed for user {UserId}", userId);
        }
    }
}
