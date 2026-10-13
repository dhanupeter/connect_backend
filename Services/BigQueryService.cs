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

    public async Task InsertConversationAsync(Conversation conv)
    {
        if (_client == null) return;
        try
        {
            var row = new BigQueryInsertRow
            {
                ["conversation_id"] = conv.ConversationId,
                ["is_group"] = conv.IsGroup,
                ["title"] = conv.Title,
                ["description"] = conv.Description ?? "",
                ["avatar_url"] = conv.AvatarUrl ?? "",
                ["scope_radius_km"] = conv.ScopeRadiusKm,
                ["category"] = conv.Category ?? "General",
                ["created_by_user_id"] = conv.CreatedByUserId ?? 0,
                ["participant_count"] = conv.ParticipantCount > 0 ? conv.ParticipantCount : (conv.IsGroup ? 1 : 2),
                ["created_at"] = (conv.CreatedAt == default ? DateTime.UtcNow : conv.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            await _client.InsertRowAsync(DatasetId, "chat_conversations", row);
            _logger.LogInformation("Conversation {ConversationId} inserted into BigQuery chat_conversations", conv.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert conversation into BigQuery.");
        }
    }

    public async Task<IEnumerable<Conversation>> GetConversationsAsync(long userId)
    {
        if (_client == null) return Enumerable.Empty<Conversation>();
        try
        {
            var sql = $@"
                SELECT 
                    c.conversation_id,
                    c.is_group,
                    c.title,
                    c.description,
                    c.avatar_url,
                    c.scope_radius_km,
                    c.category,
                    c.created_by_user_id,
                    c.participant_count,
                    c.created_at,
                    c.updated_at,
                    lm.message_text AS last_message,
                    lm.created_at AS last_message_time
                FROM `{ProjectId}.{DatasetId}.chat_conversations` c
                LEFT JOIN (
                    SELECT conversation_id, message_text, created_at
                    FROM (
                        SELECT 
                            conversation_id, 
                            message_text, 
                            created_at,
                            ROW_NUMBER() OVER(PARTITION BY conversation_id ORDER BY created_at DESC) as rn
                        FROM `{ProjectId}.{DatasetId}.chat_messages`
                    )
                    WHERE rn = 1
                ) lm ON c.conversation_id = lm.conversation_id
                ORDER BY c.updated_at DESC
            ";

            var results = await _client.ExecuteQueryAsync(sql, null);
            var list = new List<Conversation>();

            foreach (var r in results)
            {
                list.Add(new Conversation
                {
                    ConversationId = r["conversation_id"]?.ToString() ?? "",
                    IsGroup = Convert.ToBoolean(r["is_group"]),
                    Title = r["title"]?.ToString() ?? "",
                    Description = r["description"]?.ToString(),
                    AvatarUrl = r["avatar_url"]?.ToString(),
                    ScopeRadiusKm = Convert.ToInt32(r["scope_radius_km"] ?? 100),
                    Category = r["category"]?.ToString() ?? "General",
                    CreatedByUserId = r["created_by_user_id"] != null ? Convert.ToInt64(r["created_by_user_id"]) : null,
                    ParticipantCount = Convert.ToInt32(r["participant_count"] ?? 1),
                    CreatedAt = r["created_at"] is DateTime dt ? dt : DateTime.UtcNow,
                    UpdatedAt = r["updated_at"] is DateTime udt ? udt : DateTime.UtcNow,
                    LastMessage = r["last_message"]?.ToString() ?? "Live connected",
                    LastMessageTime = r["last_message_time"] is DateTime ldt ? ldt : DateTime.UtcNow,
                    IsOnline = true
                });
            }

            // Groups: Only return groups the user belongs to or created, or public groups with active members
            var memberGroupsSql = $@"
                SELECT DISTINCT conversation_id FROM `{ProjectId}.{DatasetId}.chat_conversation_participants`
                WHERE user_id = {userId}";
            var memberGroupIds = new HashSet<string>();
            try
            {
                var mgRows = await _client.ExecuteQueryAsync(memberGroupsSql, null);
                foreach (var mr in mgRows)
                {
                    var gid = mr["conversation_id"]?.ToString();
                    if (!string.IsNullOrEmpty(gid)) memberGroupIds.Add(gid);
                }
            }
            catch { }

            // Filter groups: show if user is participant, created it, or group has members
            list = list.Where(c => c.IsGroup && (c.CreatedByUserId == userId || memberGroupIds.Contains(c.ConversationId) || c.ParticipantCount > 0)).ToList();

            // Direct chats: derived from messages (conversation id = chat_{minId}_{maxId})
            var dmSql = $@"
                SELECT conversation_id, message_text, created_at
                FROM (
                    SELECT conversation_id, message_text, created_at,
                        ROW_NUMBER() OVER(PARTITION BY conversation_id ORDER BY created_at DESC) AS rn
                    FROM `{ProjectId}.{DatasetId}.chat_messages`
                    WHERE STARTS_WITH(conversation_id, 'chat_')
                )
                WHERE rn = 1
                ORDER BY created_at DESC";
            var dmRows = await _client.ExecuteQueryAsync(dmSql, null);
            var contacts = (await GetContactsAsync()).GroupBy(u => u.UserId).ToDictionary(g => g.Key, g => g.First());

            // Check user preferences for muted / deleted / blocked
            var prefsSql = $@"SELECT conversation_id, is_muted, is_deleted, is_blocked FROM `{ProjectId}.{DatasetId}.user_preferences` WHERE user_id = {userId}";
            var prefMap = new Dictionary<string, (bool Muted, bool Deleted, bool Blocked)>();
            try
            {
                var prefRows = await _client.ExecuteQueryAsync(prefsSql, null);
                foreach (var pr in prefRows)
                {
                    var pcid = pr["conversation_id"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(pcid))
                    {
                        prefMap[pcid] = (
                            Convert.ToBoolean(pr["is_muted"] ?? false),
                            Convert.ToBoolean(pr["is_deleted"] ?? false),
                            Convert.ToBoolean(pr["is_blocked"] ?? false)
                        );
                    }
                }
            }
            catch { }

            foreach (var r in dmRows)
            {
                var cid = r["conversation_id"]?.ToString() ?? "";
                var parts = cid.Split('_');
                if (parts.Length != 3 || !long.TryParse(parts[1], out var a) || !long.TryParse(parts[2], out var b)) continue;
                if (a != userId && b != userId) continue;
                var otherId = a == userId ? b : a;
                if (!contacts.TryGetValue(otherId, out var other)) continue; // Only real registered users

                if (prefMap.TryGetValue(cid, out var pref) && (pref.Deleted || pref.Blocked))
                {
                    continue; // Skipped because user deleted or blocked this conversation
                }

                var ts = r["created_at"] is DateTime dt2 ? dt2 : DateTime.UtcNow;
                list.Add(new Conversation
                {
                    ConversationId = cid,
                    IsGroup = false,
                    Title = other.Name,
                    AvatarUrl = other.AvatarUrl ?? "https://lh3.googleusercontent.com/a/default-user",
                    OtherUserId = otherId,
                    ParticipantCount = 2,
                    CarModel = other.CarModel,
                    CreatedAt = ts,
                    UpdatedAt = ts,
                    LastMessage = r["message_text"]?.ToString(),
                    LastMessageTime = ts,
                    IsOnline = string.Equals(other.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
                });
            }

            return list.OrderByDescending(c => c.LastMessageTime ?? c.UpdatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get conversations from BigQuery.");
            return Enumerable.Empty<Conversation>();
        }
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId, int limit)
    {
        if (_client == null) return Enumerable.Empty<ChatMessage>();
        try
        {
            var sql = $@"
                SELECT 
                    message_id,
                    conversation_id,
                    sender_user_id,
                    sender_name,
                    sender_avatar,
                    message_text,
                    message_type,
                    attachment_url,
                    file_name,
                    file_size,
                    metadata_json,
                    status,
                    created_at
                FROM `{ProjectId}.{DatasetId}.chat_messages`
                WHERE conversation_id = @conv_id
                ORDER BY created_at DESC
                LIMIT {Math.Clamp(limit, 1, 200)}
            ";

            var parameters = new[]
            {
                new BigQueryParameter("conv_id", BigQueryDbType.String, conversationId)
            };

            var results = await _client.ExecuteQueryAsync(sql, parameters);
            var list = new List<ChatMessage>();

            foreach (var r in results)
            {
                list.Add(new ChatMessage
                {
                    MessageId = r["message_id"]?.ToString() ?? "",
                    ConversationId = r["conversation_id"]?.ToString() ?? "",
                    SenderUserId = Convert.ToInt64(r["sender_user_id"] ?? 0),
                    SenderName = r["sender_name"]?.ToString() ?? "Driver",
                    SenderAvatar = r["sender_avatar"]?.ToString(),
                    MessageText = r["message_text"]?.ToString() ?? "",
                    MessageType = r["message_type"]?.ToString() ?? "text",
                    AttachmentUrl = r["attachment_url"]?.ToString(),
                    FileName = r["file_name"]?.ToString(),
                    FileSize = r["file_size"] != null ? Convert.ToInt64(r["file_size"]) : null,
                    MetadataJson = r["metadata_json"]?.ToString(),
                    Status = r["status"]?.ToString() ?? "delivered",
                    CreatedAt = r["created_at"] is DateTime dt ? dt : DateTime.UtcNow
                });
            }

            return list.OrderBy(m => m.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get messages from BigQuery for conversation {ConvId}.", conversationId);
            return Enumerable.Empty<ChatMessage>();
        }
    }

    public async Task<IEnumerable<NearbyUserDto>> GetContactsAsync()
    {
        if (_client == null) return Enumerable.Empty<NearbyUserDto>();
        try
        {
            var sql = $@"
                SELECT 
                    user_id,
                    first_name,
                    last_name,
                    email,
                    profile_image_url,
                    latitude,
                    longitude,
                    status,
                    status_description,
                    last_login_at
                FROM `{ProjectId}.{DatasetId}.identity_users`
                WHERE user_id IS NOT NULL AND user_id > 0
                QUALIFY ROW_NUMBER() OVER(PARTITION BY user_id ORDER BY last_login_at DESC) = 1
                ORDER BY user_id ASC
            ";

            var results = await _client.ExecuteQueryAsync(sql, null);
            var list = new List<NearbyUserDto>();

            foreach (var r in results)
            {
                var fName = r["first_name"]?.ToString() ?? "";
                var lName = r["last_name"]?.ToString() ?? "";
                var fullName = $"{fName} {lName}".Trim();
                if (string.IsNullOrWhiteSpace(fullName)) fullName = "User";

                list.Add(new NearbyUserDto
                {
                    UserId = Convert.ToInt64(r["user_id"] ?? 0),
                    Name = fullName,
                    Email = r["email"]?.ToString() ?? "",
                    AvatarUrl = r["profile_image_url"]?.ToString() ?? "https://lh3.googleusercontent.com/a/default-user",
                    CarModel = "Electric Vehicle",
                    Plate = "TN EV " + (r["user_id"]?.ToString() ?? "100"),
                    Soc = 85,
                    Latitude = r["latitude"] != null ? Convert.ToDouble(r["latitude"]) : 0,
                    Longitude = r["longitude"] != null ? Convert.ToDouble(r["longitude"]) : 0,
                    DistanceKm = 0,
                    LastActiveAt = r["last_login_at"] is DateTime lla ? DateTime.SpecifyKind(lla, DateTimeKind.Utc) : DateTime.MinValue,
                    Status = r["status"]?.ToString() ?? "ACTIVE",
                    StatusDescription = r["status_description"]?.ToString() ?? "Available"
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get contacts from BigQuery.");
            return Enumerable.Empty<NearbyUserDto>();
        }
    }

    public async Task<IEnumerable<NearbyUserDto>> GetNearbyUsersAsync(double lat, double lng, double radiusKm)
    {
        var all = await GetContactsAsync();
        var nearby = new List<NearbyUserDto>();

        foreach (var u in all)
        {
            // Skip users who explicitly opted out of discovery
            if (string.Equals(u.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase)) continue;

            double dist = 0;
            if (u.Latitude != 0 && u.Longitude != 0 && lat != 0 && lng != 0)
            {
                dist = CalculateHaversine(lat, lng, u.Latitude, u.Longitude);
            }

            if (radiusKm <= 0 || radiusKm >= 1000 || dist <= radiusKm)
            {
                u.DistanceKm = Math.Round(dist, 1);
                u.Status = "Online";
                nearby.Add(u);
            }
        }

        return nearby.OrderBy(u => u.DistanceKm);
    }

    public async Task SetDiscoverableAsync(long userId, bool isDiscoverable)
    {
        if (_client == null) return;
        try
        {
            var sql = $@"
                UPDATE `{ProjectId}.{DatasetId}.identity_users`
                SET status = CASE WHEN @disc THEN 'ACTIVE' ELSE 'INACTIVE' END,
                    updated_at = CURRENT_TIMESTAMP()
                WHERE user_id = @uid";
            await _client.ExecuteQueryAsync(sql, new[]
            {
                new BigQueryParameter("disc", BigQueryDbType.Bool, isDiscoverable),
                new BigQueryParameter("uid", BigQueryDbType.Int64, userId)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set discoverable flag in BigQuery.");
        }
    }

    public async Task UpdateUserLocationAsync(long userId, double lat, double lng, bool isDiscoverable)
    {
        if (_client == null) return;
        try
        {
            var sql = $@"
                UPDATE `{ProjectId}.{DatasetId}.identity_users`
                SET 
                    latitude = @lat,
                    longitude = @lng,
                    last_login_at = CURRENT_TIMESTAMP(),
                    updated_at = CURRENT_TIMESTAMP(),
                    status = CASE WHEN @disc THEN 'ACTIVE' ELSE 'INACTIVE' END
                WHERE user_id = @uid
            ";

            var parameters = new[]
            {
                new BigQueryParameter("lat", BigQueryDbType.Float64, lat),
                new BigQueryParameter("lng", BigQueryDbType.Float64, lng),
                new BigQueryParameter("disc", BigQueryDbType.Bool, isDiscoverable),
                new BigQueryParameter("uid", BigQueryDbType.Int64, userId)
            };

            await _client.ExecuteQueryAsync(sql, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user location in BigQuery.");
        }
    }

    private static double CalculateHaversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth radius in km
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

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

    private bool _devicesTableReady;

    private async Task EnsureDevicesTableAsync()
    {
        if (_client == null || _devicesTableReady) return;
        await _client.ExecuteQueryAsync($@"
            CREATE TABLE IF NOT EXISTS `{ProjectId}.{DatasetId}.user_devices` (
                user_id INT64,
                device_id STRING,
                fcm_token STRING,
                platform STRING,
                manufacturer STRING,
                model STRING,
                os_version STRING,
                app_version STRING,
                created_at TIMESTAMP,
                last_seen_at TIMESTAMP
            )", null);
        _devicesTableReady = true;
    }

    public async Task RegisterDeviceAsync(DeviceInfoDto d)
    {
        if (_client == null) return;
        try
        {
            await EnsureDevicesTableAsync();
            var sql = $@"
                MERGE `{ProjectId}.{DatasetId}.user_devices` T
                USING (SELECT @uid AS user_id, @did AS device_id) S
                ON T.user_id = S.user_id AND T.device_id = S.device_id
                WHEN MATCHED THEN UPDATE SET fcm_token = @tok, platform = @plat, manufacturer = @man,
                    model = @mod, os_version = @os, app_version = @app, last_seen_at = CURRENT_TIMESTAMP()
                WHEN NOT MATCHED THEN INSERT (user_id, device_id, fcm_token, platform, manufacturer, model, os_version, app_version, created_at, last_seen_at)
                    VALUES (@uid, @did, @tok, @plat, @man, @mod, @os, @app, CURRENT_TIMESTAMP(), CURRENT_TIMESTAMP())";
            await _client.ExecuteQueryAsync(sql, new[]
            {
                new BigQueryParameter("uid", BigQueryDbType.Int64, d.UserId),
                new BigQueryParameter("did", BigQueryDbType.String, d.DeviceId ?? ""),
                new BigQueryParameter("tok", BigQueryDbType.String, d.FcmToken ?? ""),
                new BigQueryParameter("plat", BigQueryDbType.String, d.Platform ?? ""),
                new BigQueryParameter("man", BigQueryDbType.String, d.Manufacturer ?? ""),
                new BigQueryParameter("mod", BigQueryDbType.String, d.Model ?? ""),
                new BigQueryParameter("os", BigQueryDbType.String, d.OsVersion ?? ""),
                new BigQueryParameter("app", BigQueryDbType.String, d.AppVersion ?? "")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register device in BigQuery.");
        }
    }

    public async Task<IEnumerable<string>> GetDeviceTokensAsync(long userId)
    {
        if (_client == null) return Enumerable.Empty<string>();
        try
        {
            await EnsureDevicesTableAsync();
            var rows = await _client.ExecuteQueryAsync($@"
                SELECT DISTINCT fcm_token FROM `{ProjectId}.{DatasetId}.user_devices`
                WHERE user_id = @uid AND fcm_token IS NOT NULL AND fcm_token != ''",
                new[] { new BigQueryParameter("uid", BigQueryDbType.Int64, userId) });
            return rows.Select(r => r["fcm_token"]?.ToString() ?? "").Where(t => t != "").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read device tokens.");
            return Enumerable.Empty<string>();
        }
    }

    private const string Base64ServiceAccountKey = "ewogICJ0eXBlIjogInNlcnZpY2VfYWNjb3VudCIsCiAgInByb2plY3RfaWQiOiAiZXZlbnQtNTA2MTE3IiwKICAicHJpdmF0ZV9rZXlfaWQiOiAiY2ZkNGY1NzVlMTNiMGJiNjllYTRiM2JiZDAxZGY0Yjc0M2NiNDVkYiIsCiAgInByaXZhdGVfa2V5IjogIi0tLS0tQkVHSU4gUFJJVkFURSBLRVktLS0tLVxuTUlJRXZnSUJBREFOQmdrcWhraUc5dzBCQVFFRkFBU0NCS2d3Z2dTa0FnRUFBb0lCQVFDbVpKUk5QZ3o0QXRrOVxuMHdaR0tnUGZxcTk1cE1NQVEzUXJnNHBHSDBWTnlmWUpaVXI5dEhWZ1k1NlZuVW8zalQrRnhGMnhkcTVmN3orVlxucCtNc2crZ2MzZ0JCMFhWR09qNzNzQ3NweFNIT1NRdG1pRjdVYzBCdE5uMEw4ZGFtWm9NNkxYdGJDRjlRL3pUQ1xuMjZXWkJsNFAxcndrOGMxeExqRTZ1U2VSY2JRRGdBdUw1cm10T3FQMy9ZQ1hOOUZMeEVYdzNNaDVzclNvbFhlS1xuU25PMjFFN0IxSWVrOFFxVS9wQWJkeC9LRGFNeWdnbHBhQnIxeU44b29pVjd3RERXQlVjd29aZnlGL1crdFo0ZFxuNFlkaWF5OThmWUVobVBxejVMdUZjYUlENTRhUmd1dXhweEVZQXpFcjl3SENmWnkxcHJZemVFdmtMSHhxT09ZRVxuTDVKNDBkTnRBZ01CQUFFQ2dnRUFCT0xhT2dSeTlBdDlYcVBtUU1RMkZlall1MDA3enJPc1l5UzdPRUR0MDVGMVxuTUtSeFZjN3BUWG5QYTl4UkZCMkpPbi9nTW5obU9YbjRsNGYxRTFjU1IxcTdqak04NnVHRi9OY0hCWFI5QUNaRFxuOS9CVVBJL2YyZlVxS2VJaHFBUkZvVDA1ckp3cjFHQ1NKNUMwcGJBNnJlTmo1Zi9WeFJlU3FZTkxacWw4bkVlblxucnhFRW1DWVRqNjd4ZjFiNE5uVW5ubUJ2N0w5YTZYVTRrNlE0RUxYbVRlZTF1UnEvcHBJaHEvNHVpeGhGNHV4NFxuSlBwOVpkTW9zeWsrNk4xOWtkb2l3Qk5weGE2dk9EdFZtNXB3c1p6aDVhSE96NXVXQTYxZGtwZTV5dFQzYkV4VVxuL3NVdmxXUEgzaGViSlV6SjI2R3BlaEQxa2xGR0lEZzFlcWZzRW5KQ2Z3S0JnUURUcHI5K1BVSjI2TFhOd1g2WlxucjlaSzBGRkpodmlhNGUxMXVBQjVDQ3haVTJQeE9jc3RHL0hWOGo0aHRTdjUydUQ3T25kQVlFL3pHODlJeFlmelxuenRkU1lzQlBadTBVSXlrbUNnd25oUnpoNkR4WDAxcVYzRWJ5VndJbHpva29CSnQrMkl4STRxWkNRdkdIRG5QTVxubjJ1UXNyRDlabmFPdmtmLys4MHZQR1A4THdLQmdRREpRaHZGVzh0QTJHaDIzdmtQZzBXUXhhOEovMlN0L0tVMVxuaE1PN282KzlEbVYybGFidHVib25zQTg0Q3N2Wk5ESzBkNTZIcTJuU0lvTndXZGw0SEUxNEFIa1V5UmhGYzVHTFxua3N2UzY4dUtvbFBidytvOTFQak9UdXRvK3F2MEdSTW1kMDlIVE8wSEl4YTlqeUl6aC9ZNkYwMEtuUDNFZ0prVFxuOFhPRVlEdjNJd0tCZ1FDaTM0SWpZbjhBUklxTGdTaXNtano5SzV3eVNmU01NK3BwbWFSN2YwZXBvZHVhS2RCcFxuc1VTRVVMdHhjeHNaVmsrbDVWTVpya1E0SUNGK3JjdTJXbWttMUNtTHNQZ21jL3haY29meDdhVFJUOGNJQ3ZSOVxudUpxOEtTclU3MEJyRkVpWTc0bmRHTlc2emd0UjFGUldsaVBQb1VORWJaSTB2TWRwZlZ0ankwUmlMd0tCZ0FHbFxuWU1URWMyMGNWLzVnNUhpVUFrV2syRUdrclJZZk10cTFNMFNkbWhwWnU4cTBmcUVERzlxblBmaWZYZnR3eWRsZ1xuQVJQcSt2cVUvYTQxamVuRmtYeWh4RnUvS1V1b0hsQ2Q2cnpXNE50SmFKWXJqQWJsYndPQThidnlFT1gyTG9ucFxuLytQOVpSTGgzcFcrVGZGTjgxMldRek1MQmFoTnhQSzh6czBPWEc4eEFvR0JBSXhKbXVXb3NrVUZXdndLUGJoMFxuL05wWkZnaDkySkFKZ0lpLzRCNlMzcFVEZlA2czFJNngwWDZQUUE3NzkxSE1STEVCMXZMdjhCWTY0Y0hEZXJxZ1xucWNJLzBsdldleStGbWU4c25nTkI5TDdlWUxVbmpKcXkvY2NYcUhVb3RWazE0ajlmQlJmSWlCYkRjN2JIelQvbFxuRktzOThPTFJFRk5KUXNxOGpUazdMT0NtXG4tLS0tLUVORCBQUklWQVRFIEtFWS0tLS0tXG4iLAogICJjbGllbnRfZW1haWwiOiAiZXZlbnQtYmlncXVlcnktYWRtaW5AZXZlbnQtNTA2MTE3LmlhbS5nc2VydmljZWFjY291bnQuY29tIiwKICAiY2xpZW50X2lkIjogIjExMzMzNTQzOTA3MTIwMTIxNDcyMyIsCiAgImF1dGhfdXJpIjogImh0dHBzOi8vYWNjb3VudHMuZ29vZ2xlLmNvbS9vL29hdXRoMi9hdXRoIiwKICAidG9rZW5fdXJpIjogImh0dHBzOi8vb2F1dGgyLmdvb2dsZWFwaXMuY29tL3Rva2VuIiwKICAiYXV0aF9wcm92aWRlcl94NTA5X2NlcnRfdXJsIjogImh0dHBzOi8vd3d3Lmdvb2dsZWFwaXMuY29tL29hdXRoMi92MS9jZXJ0cyIsCiAgImNsaWVudF94NTA5X2NlcnRfdXJsIjogImh0dHBzOi8vd3d3Lmdvb2dsZWFwaXMuY29tL3JvYm90L3YxL21ldGFkYXRhL3g1MDkvZXZlbnQtYmlncXVlcnktYWRtaW4lNDBldmVudC01MDYxMTcuaWFtLmdzZXJ2aWNlYWNjb3VudC5jb20iLAogICJ1bml2ZXJzZV9kb21haW4iOiAiZ29vZ2xlYXBpcy5jb20iCn0K";
}

