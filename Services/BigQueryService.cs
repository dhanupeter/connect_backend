using ConnectBackend.Models;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;

namespace ConnectBackend.Services;

public class BigQueryService : IBigQueryService
{
    private readonly ILogger<BigQueryService> _logger;
    private readonly BigQueryClient? _client;
    public string ProjectId { get; }
    public string DatasetId { get; }
    public bool IsConfigured => _client != null;

    public BigQueryService(IConfiguration config, ILogger<BigQueryService> logger)
    {
        _logger = logger;
        ProjectId = config["BigQuery:ProjectId"] ?? Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "event-506117";
        DatasetId = config["BigQuery:DatasetId"] ?? Environment.GetEnvironmentVariable("GCP_DATASET_ID") ?? "connectdb";

        try
        {
            var keyPath = config["BigQuery:CredentialsPath"] ?? "gcp-key.json";
            var envJson = Environment.GetEnvironmentVariable("GCP_SERVICE_ACCOUNT_KEY");

            GoogleCredential? credential = null;

            if (!string.IsNullOrWhiteSpace(envJson))
            {
                credential = GoogleCredential.FromJson(envJson);
            }
            else if (File.Exists(keyPath))
            {
                credential = GoogleCredential.FromFile(keyPath);
            }
            else
            {
                var homeKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "event-506117-cfd4f575e13b.json");
                if (File.Exists(homeKey))
                {
                    credential = GoogleCredential.FromFile(homeKey);
                }
            }

            if (credential != null)
            {
                _client = BigQueryClient.Create(ProjectId, credential);
                _logger.LogInformation("BigQueryClient successfully initialized for project {ProjectId}, dataset {DatasetId}", ProjectId, DatasetId);
            }
            else
            {
                _logger.LogWarning("No Google Cloud credentials found. BigQuery features will operate in disconnected mode.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize BigQueryClient.");
        }
    }

    public async Task<BigQueryResults?> ExecuteQueryAsync(string sql)
    {
        if (_client == null) return null;
        try
        {
            return await _client.ExecuteQueryAsync(sql, parameters: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigQuery ExecuteQueryAsync error on query: {Sql}", sql);
            return null;
        }
    }

    public async Task<IEnumerable<string>> GetTablesAsync()
    {
        if (_client == null) return Enumerable.Empty<string>();
        try
        {
            var dataset = await _client.GetDatasetAsync(DatasetId);
            var tables = _client.ListTables(dataset.Reference);
            return tables.Select(t => t.Reference.TableId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigQuery GetTablesAsync error.");
            return Enumerable.Empty<string>();
        }
    }

    public async Task<long> GetTableRowCountAsync(string tableName)
    {
        if (_client == null) return 0;
        try
        {
            var table = await _client.GetTableAsync(DatasetId, tableName);
            return (long)(table.Resource.NumRows ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigQuery GetTableRowCountAsync error for table {Table}", tableName);
            return 0;
        }
    }

    public async Task InsertChatMessageAsync(ChatMessage message)
    {
        if (_client == null) return;
        try
        {
            var row = new BigQueryInsertRow
            {
                ["message_id"] = message.MessageId,
                ["conversation_id"] = message.ConversationId,
                ["sender_user_id"] = message.SenderUserId,
                ["sender_name"] = message.SenderName,
                ["sender_avatar"] = message.SenderAvatar ?? "",
                ["message_text"] = message.MessageText,
                ["message_type"] = message.MessageType,
                ["attachment_url"] = message.AttachmentUrl ?? "",
                ["file_name"] = message.FileName ?? "",
                ["file_size"] = message.FileSize ?? 0,
                ["metadata_json"] = message.MetadataJson ?? "",
                ["status"] = message.Status,
                ["created_at"] = (message.CreatedAt == default ? DateTime.UtcNow : message.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            await _client.InsertRowAsync(DatasetId, "chat_messages", row);
            _logger.LogInformation("Chat message {MessageId} streamed to BigQuery chat_messages", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream chat message to BigQuery.");
        }
    }

    public async Task InsertTelemetryAsync(VehicleTelemetry telemetry)
    {
        if (_client == null) return;
        try
        {
            var row = new BigQueryInsertRow
            {
                ["telemetry_id"] = telemetry.TelemetryId,
                ["vehicle_id"] = telemetry.VehicleId,
                ["recorded_at"] = (telemetry.RecordedAt == default ? DateTime.UtcNow : telemetry.RecordedAt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["latitude"] = (double)telemetry.Latitude,
                ["longitude"] = (double)telemetry.Longitude,
                ["speed_kmh"] = telemetry.SpeedKmh,
                ["soc"] = telemetry.SOC,
                ["battery_voltage"] = telemetry.BatteryVoltage,
                ["battery_current"] = telemetry.BatteryCurrent,
                ["battery_temperature"] = telemetry.BatteryTemperature,
                ["motor_temperature"] = telemetry.MotorTemperature,
                ["charging_power_kw"] = telemetry.ChargingPowerKw,
                ["range_km"] = telemetry.RangeKm,
                ["odometer_km"] = telemetry.OdometerKm,
                ["charging_status"] = telemetry.ChargingStatus,
                ["ignition_status"] = telemetry.IgnitionStatus
            };

            await _client.InsertRowAsync(DatasetId, "telemetry_vehicle_telemetry", row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream vehicle telemetry to BigQuery.");
        }
    }

    public async Task InsertReservationAsync(ChargingReservation res)
    {
        if (_client == null) return;
        try
        {
            var row = new BigQueryInsertRow
            {
                ["reservation_id"] = res.ReservationId,
                ["reservation_number"] = res.ReservationNumber,
                ["user_id"] = res.UserId,
                ["vehicle_id"] = res.VehicleId,
                ["station_id"] = res.StationId,
                ["connector_id"] = res.ConnectorId,
                ["start_time"] = res.StartTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["end_time"] = res.EndTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["reservation_status"] = res.ReservationStatus,
                ["estimated_energy_kwh"] = res.EstimatedEnergyKwh,
                ["estimated_amount"] = res.EstimatedAmount,
                ["created_at"] = (res.CreatedAt == default ? DateTime.UtcNow : res.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            await _client.InsertRowAsync(DatasetId, "charging_reservations", row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream charging reservation to BigQuery.");
        }
    }
}
