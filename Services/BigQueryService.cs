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
                else
                {
                    // Fallback to embedded service account JSON for cloud runtime (Render)
                    var keyBytes = Convert.FromBase64String(Base64ServiceAccountKey);
                    credential = GoogleCredential.FromJson(System.Text.Encoding.UTF8.GetString(keyBytes));
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

    public async Task InsertOrUpdateUserAsync(User user)
    {
        if (_client == null) return;
        try
        {
            var mergeSql = $@"
                MERGE `{ProjectId}.{DatasetId}.identity_users` T
                USING (
                  SELECT 
                    @user_id AS user_id,
                    @firebase_uid AS firebase_uid,
                    @user_code AS user_code,
                    @first_name AS first_name,
                    @last_name AS last_name,
                    @email AS email,
                    @mobile_number AS mobile_number,
                    @profile_image_url AS profile_image_url,
                    @status AS status,
                    @is_email_verified AS is_email_verified,
                    @is_mobile_verified AS is_mobile_verified,
                    @latitude AS latitude,
                    @longitude AS longitude
                ) S
                ON (T.email IS NOT NULL AND LOWER(T.email) = LOWER(S.email))
                   OR (T.firebase_uid IS NOT NULL AND T.firebase_uid = S.firebase_uid AND S.firebase_uid != '')
                WHEN MATCHED THEN
                  UPDATE SET 
                    last_login_at = CURRENT_TIMESTAMP(),
                    updated_at = CURRENT_TIMESTAMP(),
                    visit_count = COALESCE(T.visit_count, 1) + 1,
                    first_name = COALESCE(S.first_name, T.first_name),
                    last_name = COALESCE(S.last_name, T.last_name),
                    profile_image_url = COALESCE(S.profile_image_url, T.profile_image_url),
                    status = 'ACTIVE'
                WHEN NOT MATCHED THEN
                  INSERT (user_id, firebase_uid, user_code, first_name, last_name, email, mobile_number, profile_image_url, status, is_email_verified, is_mobile_verified, latitude, longitude, last_login_at, created_at, updated_at, visit_count)
                  VALUES (S.user_id, S.firebase_uid, S.user_code, S.first_name, S.last_name, S.email, S.mobile_number, S.profile_image_url, S.status, S.is_email_verified, S.is_mobile_verified, S.latitude, S.longitude, CURRENT_TIMESTAMP(), CURRENT_TIMESTAMP(), CURRENT_TIMESTAMP(), 1);
            ";

            var parameters = new[]
            {
                new BigQueryParameter("user_id", BigQueryDbType.Int64, user.UserId),
                new BigQueryParameter("firebase_uid", BigQueryDbType.String, user.FirebaseUid ?? ""),
                new BigQueryParameter("user_code", BigQueryDbType.String, user.UserCode ?? ("USR-" + Guid.NewGuid().ToString("N")[..8].ToUpper())),
                new BigQueryParameter("first_name", BigQueryDbType.String, user.FirstName ?? "EV"),
                new BigQueryParameter("last_name", BigQueryDbType.String, user.LastName ?? "Driver"),
                new BigQueryParameter("email", BigQueryDbType.String, user.Email ?? ""),
                new BigQueryParameter("mobile_number", BigQueryDbType.String, user.MobileNumber ?? "+91 98765 43210"),
                new BigQueryParameter("profile_image_url", BigQueryDbType.String, user.ProfileImageUrl ?? ""),
                new BigQueryParameter("status", BigQueryDbType.String, user.Status ?? "ACTIVE"),
                new BigQueryParameter("is_email_verified", BigQueryDbType.Bool, user.IsEmailVerified),
                new BigQueryParameter("is_mobile_verified", BigQueryDbType.Bool, user.IsMobileVerified),
                new BigQueryParameter("latitude", BigQueryDbType.Float64, 11.0168),
                new BigQueryParameter("longitude", BigQueryDbType.Float64, 76.9558),
            };

            await _client.ExecuteQueryAsync(mergeSql, parameters);
            _logger.LogInformation("Driver {UserId} ({Email}) logged in: tracked visit in BigQuery identity_users (no duplicates)!", user.UserId, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert user {UserId} to BigQuery.", user.UserId);
        }
    }

    public async Task InsertOrUpdateVehicleAsync(Vehicle vehicle)
    {
        if (_client == null) return;
        try
        {
            var mergeSql = $@"
                MERGE `{ProjectId}.{DatasetId}.vehicle_vehicles` T
                USING (
                  SELECT 
                    @vehicle_id AS vehicle_id,
                    @user_id AS user_id,
                    @vehicle_code AS vehicle_code,
                    @registration_number AS registration_number,
                    @vin AS vin,
                    @manufacturer_id AS manufacturer_id,
                    @model_id AS model_id,
                    @variant_id AS variant_id,
                    @color AS color,
                    @is_electric AS is_electric,
                    @status AS status
                ) S
                ON T.user_id = S.user_id OR (T.registration_number IS NOT NULL AND T.registration_number = S.registration_number)
                WHEN MATCHED THEN
                  UPDATE SET 
                    updated_at = CURRENT_TIMESTAMP(),
                    registration_number = COALESCE(S.registration_number, T.registration_number),
                    color = COALESCE(S.color, T.color),
                    status = 'ACTIVE'
                WHEN NOT MATCHED THEN
                  INSERT (vehicle_id, user_id, vehicle_code, registration_number, vin, manufacturer_id, model_id, variant_id, color, is_electric, status, created_at, updated_at)
                  VALUES (S.vehicle_id, S.user_id, S.vehicle_code, S.registration_number, S.vin, S.manufacturer_id, S.model_id, S.variant_id, S.color, S.is_electric, S.status, CURRENT_TIMESTAMP(), CURRENT_TIMESTAMP());
            ";

            var parameters = new[]
            {
                new BigQueryParameter("vehicle_id", BigQueryDbType.Int64, vehicle.VehicleId),
                new BigQueryParameter("user_id", BigQueryDbType.Int64, vehicle.UserId),
                new BigQueryParameter("vehicle_code", BigQueryDbType.String, vehicle.VehicleCode ?? ("VEH-" + Guid.NewGuid().ToString("N")[..8].ToUpper())),
                new BigQueryParameter("registration_number", BigQueryDbType.String, vehicle.RegistrationNumber ?? "TN 38 EV 8899"),
                new BigQueryParameter("vin", BigQueryDbType.String, vehicle.VIN ?? ("MAT" + Guid.NewGuid().ToString("N")[..14].ToUpper())),
                new BigQueryParameter("manufacturer_id", BigQueryDbType.Int64, (long)(vehicle.ManufacturerId > 0 ? vehicle.ManufacturerId : 1)),
                new BigQueryParameter("model_id", BigQueryDbType.Int64, (long)(vehicle.ModelId > 0 ? vehicle.ModelId : 1)),
                new BigQueryParameter("variant_id", BigQueryDbType.Int64, (long)(vehicle.VariantId ?? 1)),
                new BigQueryParameter("color", BigQueryDbType.String, vehicle.Color ?? "Teal Blue"),
                new BigQueryParameter("is_electric", BigQueryDbType.Bool, vehicle.IsElectric),
                new BigQueryParameter("status", BigQueryDbType.String, vehicle.Status ?? "ACTIVE"),
            };

            await _client.ExecuteQueryAsync(mergeSql, parameters);
            _logger.LogInformation("Vehicle {VehicleId} ({Plate}) upserted in BigQuery vehicle_vehicles (no duplicates)!", vehicle.VehicleId, vehicle.RegistrationNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert vehicle {VehicleId} to BigQuery.", vehicle.VehicleId);
        }
    }

    private const string Base64ServiceAccountKey = "ewogICJ0eXBlIjogInNlcnZpY2VfYWNjb3VudCIsCiAgInByb2plY3RfaWQiOiAiZXZlbnQtNTA2MTE3IiwKICAicHJpdmF0ZV9rZXlfaWQiOiAiY2ZkNGY1NzVlMTNiMGJiNjllYTRiM2JiZDAxZGY0Yjc0M2NiNDVkYiIsCiAgInByaXZhdGVfa2V5IjogIi0tLS0tQkVHSU4gUFJJVkFURSBLRVktLS0tLVxuTUlJRXZnSUJBREFOQmdrcWhraUc5dzBCQVFFRkFBU0NCS2d3Z2dTa0FnRUFBb0lCQVFDbVpKUk5QZ3o0QXRrOVxuMHdaR0tnUGZxcTk1cE1NQVEzUXJnNHBHSDBWTnlmWUpaVXI5dEhWZ1k1NlZuVW8zalQrRnhGMnhkcTVmN3orVlxucCtNc2crZ2MzZ0JCMFhWR09qNzNzQ3NweFNIT1NRdG1pRjdVYzBCdE5uMEw4ZGFtWm9NNkxYdGJDRjlRL3pUQ1xuMjZXWkJsNFAxcndrOGMxeExqRTZ1U2VSY2JRRGdBdUw1cm10T3FQMy9ZQ1hOOUZMeEVYdzNNaDVzclNvbFhlS1xuU25PMjFFN0IxSWVrOFFxVS9wQWJkeC9LRGFNeWdnbHBhQnIxeU44b29pVjd3RERXQlVjd29aZnlGL1crdFo0ZFxuNFlkaWF5OThmWUVobVBxejVMdUZjYUlENTRhUmd1dXhweEVZQXpFcjl3SENmWnkxcHJZemVFdmtMSHhxT09ZRVxuTDVKNDBkTnRBZ01CQUFFQ2dnRUFCT0xhT2dSeTlBdDlYcVBtUU1RMkZlall1MDA3enJPc1l5UzdPRUR0MDVGMVxuTUtSeFZjN3BUWG5QYTl4UkZCMkpPbi9nTW5obU9YbjRsNGYxRTFjU1IxcTdqak04NnVHRi9OY0hCWFI5QUNaRFxuOS9CVVBJL2YyZlVxS2VJaHFBUkZvVDA1ckp3cjFHQ1NKNUMwcGJBNnJlTmo1Zi9WeFJlU3FZTkxacWw4bkVlblxucnhFRW1DWVRqNjd4ZjFiNE5uVW5ubUJ2N0w5YTZYVTRrNlE0RUxYbVRlZTF1UnEvcHBJaHEvNHVpeGhGNHV4NFxuSlBwOVpkTW9zeWsrNk4xOWtkb2l3Qk5weGE2dk9EdFZtNXB3c1p6aDVhSE96NXVXQTYxZGtwZTV5dFQzYkV4VVxuL3NVdmxXUEgzaGViSlV6SjI2R3BlaEQxa2xGR0lEZzFlcWZzRW5KQ2Z3S0JnUURUcHI5K1BVSjI2TFhOd1g2WlxucjlaSzBGRkpodmlhNGUxMXVBQjVDQ3haVTJQeE9jc3RHL0hWOGo0aHRTdjUydUQ3T25kQVlFL3pHODlJeFlmelxuenRkU1lzQlBadTBVSXlrbUNnd25oUnpoNkR4WDAxcVYzRWJ5VndJbHpva29CSnQrMkl4STRxWkNRdkdIRG5QTVxubjJ1UXNyRDlabmFPdmtmLys4MHZQR1A4THdLQmdRREpRaHZGVzh0QTJHaDIzdmtQZzBXUXhhOEovMlN0L0tVMVxuaE1PN282KzlEbVYybGFidHVib25zQTg0Q3N2Wk5ESzBkNTZIcTJuU0lvTndXZGw0SEUxNEFIa1V5UmhGYzVHTFxua3N2UzY4dUtvbFBidytvOTFQak9UdXRvK3F2MEdSTW1kMDlIVE8wSEl4YTlqeUl6aC9ZNkYwMEtuUDNFZ0prVFxuOFhPRVlEdjNJd0tCZ1FDaTM0SWpZbjhBUklxTGdTaXNtano5SzV3eVNmU01NK3BwbWFSN2YwZXBvZHVhS2RCcFxuc1VTRVVMdHhjeHNaVmsrbDVWTVpya1E0SUNGK3JjdTJXbWttMUNtTHNQZ21jL3haY29meDdhVFJUOGNJQ3ZSOVxudUpxOEtTclU3MEJyRkVpWTc0bmRHTlc2emd0UjFGUldsaVBQb1VORWJaSTB2TWRwZlZ0ankwUmlMd0tCZ0FHbFxuWU1URWMyMGNWLzVnNUhpVUFrV2syRUdrclJZZk10cTFNMFNkbWhwWnU4cTBmcUVERzlxblBmaWZYZnR3eWRsZ1xuQVJQcSt2cVUvYTQxamVuRmtYeWh4RnUvS1V1b0hsQ2Q2cnpXNE50SmFKWXJqQWJsYndPQThidnlFT1gyTG9ucFxuLytQOVpSTGgzcFcrVGZGTjgxMldRek1MQmFoTnhQSzh6czBPWEc4eEFvR0JBSXhKbXVXb3NrVUZXdndLUGJoMFxuL05wWkZnaDkySkFKZ0lpLzRCNlMzcFVEZlA2czFJNngwWDZQUUE3NzkxSE1STEVCMXZMdjhCWTY0Y0hEZXJxZ1xucWNJLzBsdldleStGbWU4c25nTkI5TDdlWUxVbmpKcXkvY2NYcUhVb3RWazE0ajlmQlJmSWlCYkRjN2JIelQvbFxuRktzOThPTFJFRk5KUXNxOGpUazdMT0NtXG4tLS0tLUVORCBQUklWQVRFIEtFWS0tLS0tXG4iLAogICJjbGllbnRfZW1haWwiOiAiZXZlbnQtYmlncXVlcnktYWRtaW5AZXZlbnQtNTA2MTE3LmlhbS5nc2VydmljZWFjY291bnQuY29tIiwKICAiY2xpZW50X2lkIjogIjExMzMzNTQzOTA3MTIwMTIxNDcyMyIsCiAgImF1dGhfdXJpIjogImh0dHBzOi8vYWNjb3VudHMuZ29vZ2xlLmNvbS9vL29hdXRoMi9hdXRoIiwKICAidG9rZW5fdXJpIjogImh0dHBzOi8vb2F1dGgyLmdvb2dsZWFwaXMuY29tL3Rva2VuIiwKICAiYXV0aF9wcm92aWRlcl94NTA5X2NlcnRfdXJsIjogImh0dHBzOi8vd3d3Lmdvb2dsZWFwaXMuY29tL29hdXRoMi92MS9jZXJ0cyIsCiAgImNsaWVudF94NTA5X2NlcnRfdXJsIjogImh0dHBzOi8vd3d3Lmdvb2dsZWFwaXMuY29tL3JvYm90L3YxL21ldGFkYXRhL3g1MDkvZXZlbnQtYmlncXVlcnktYWRtaW4lNDBldmVudC01MDYxMTcuaWFtLmdzZXJ2aWNlYWNjb3VudC5jb20iLAogICJ1bml2ZXJzZV9kb21haW4iOiAiZ29vZ2xlYXBpcy5jb20iCn0K";
}

