using System.Collections.Concurrent;
using System.Data;
using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly IDbConnectionFactory _dbFactory;

    // In-memory fallbacks when running in containers without active external MSSQL
    private static readonly ConcurrentDictionary<string, Conversation> _memoryConversations = new();
    private static readonly ConcurrentDictionary<string, List<ChatMessage>> _memoryMessages = new();

    static ChatRepository()
    {
        // Seed default convoys & chats
        var seedGroups = new List<Conversation>
        {
            new()
            {
                ConversationId = "grp_convoy_100",
                IsGroup = true,
                Title = "Coimbatore - Bengaluru EV Convoy",
                Description = "Live convoy for Bangalore highway drivers (100km radius)",
                AvatarUrl = "https://images.unsplash.com/photo-1558441719-7d0840db04b9?auto=format&fit=crop&w=150&q=80",
                ScopeRadiusKm = 100,
                Category = "Convoy",
                CreatedByUserId = 101,
                ParticipantCount = 38,
                LastMessage = "Leaving Zeon Avinashi in 10 mins with 90% SOC ⚡",
                LastMessageTime = DateTime.UtcNow.AddMinutes(-5),
                IsOnline = true
            },
            new()
            {
                ConversationId = "grp_emergency_sos",
                IsGroup = true,
                Title = "EV SOS & Highway Rescue (100km)",
                Description = "Community charging assists & emergency flatbed dispatch",
                AvatarUrl = "https://images.unsplash.com/photo-1549399542-7e3f8b79c341?auto=format&fit=crop&w=150&q=80",
                ScopeRadiusKm = 100,
                Category = "Emergency",
                CreatedByUserId = 102,
                ParticipantCount = 142,
                LastMessage = "All clear on NH544 Salem bypass",
                LastMessageTime = DateTime.UtcNow.AddMinutes(-22),
                IsOnline = true
            },
            new()
            {
                ConversationId = "user_101",
                IsGroup = false,
                Title = "Rajesh Kannan (Tiago EV)",
                Description = "Nearby Driver - 2.4 km away",
                AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80",
                ScopeRadiusKm = 100,
                Category = "Direct",
                CreatedByUserId = 101,
                ParticipantCount = 2,
                LastMessage = "Bay 2 at Zeon Hub is available right now!",
                LastMessageTime = DateTime.UtcNow.AddMinutes(-2),
                IsOnline = true
            }
        };

        foreach (var g in seedGroups)
        {
            _memoryConversations[g.ConversationId] = g;
            _memoryMessages[g.ConversationId] = new List<ChatMessage>
            {
                new()
                {
                    MessageId = "msg_seed_1",
                    ConversationId = g.ConversationId,
                    SenderUserId = 101,
                    SenderName = "Rajesh Kannan",
                    SenderAvatar = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80",
                    MessageText = g.LastMessage ?? "Hello EV community!",
                    MessageType = "text",
                    Status = "read",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                }
            };
        }
    }

    public ChatRepository(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IEnumerable<Conversation>> GetConversationsAsync(long userId)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                SELECT 
                    c.ConversationId,
                    c.IsGroup,
                    c.Title,
                    c.Description,
                    c.AvatarUrl,
                    c.ScopeRadiusKm,
                    c.Category,
                    c.CreatedByUserId,
                    c.CreatedAt,
                    c.UpdatedAt,
                    (SELECT TOP 1 m.MessageText FROM chat.Messages m WHERE m.ConversationId = c.ConversationId ORDER BY m.CreatedAt DESC) AS LastMessage,
                    (SELECT TOP 1 m.CreatedAt FROM chat.Messages m WHERE m.ConversationId = c.ConversationId ORDER BY m.CreatedAt DESC) AS LastMessageTime,
                    ISNULL((SELECT COUNT(1) FROM chat.ConversationParticipants cp WHERE cp.ConversationId = c.ConversationId), 
                           CASE WHEN c.IsGroup = 1 THEN 18 ELSE 2 END) AS ParticipantCount,
                    0 AS UnreadCount,
                    1 AS IsOnline
                FROM chat.Conversations c
                ORDER BY ISNULL((SELECT TOP 1 m.CreatedAt FROM chat.Messages m WHERE m.ConversationId = c.ConversationId ORDER BY m.CreatedAt DESC), c.CreatedAt) DESC;
            ";

            var list = await conn.QueryAsync<Conversation>(sql);
            if (list.Any()) return list;
        }
        catch
        {
            // Fallback to in-memory conversations if DB is unreachable
        }

        return _memoryConversations.Values.OrderByDescending(c => c.LastMessageTime ?? c.CreatedAt);
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 50)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                SELECT TOP (@Limit)
                    MessageId,
                    ConversationId,
                    SenderUserId,
                    SenderName,
                    SenderAvatar,
                    MessageText,
                    MessageType,
                    AttachmentUrl,
                    FileName,
                    FileSize,
                    MetadataJson,
                    Status,
                    CreatedAt
                FROM chat.Messages
                WHERE ConversationId = @ConversationId
                ORDER BY CreatedAt ASC;
            ";

            var list = await conn.QueryAsync<ChatMessage>(sql, new { ConversationId = conversationId, Limit = limit });
            if (list.Any()) return list;
        }
        catch
        {
            // Fallback to in-memory messages
        }

        if (_memoryMessages.TryGetValue(conversationId, out var msgs))
        {
            return msgs.TakeLast(limit);
        }

        return Enumerable.Empty<ChatMessage>();
    }

    public async Task<ChatMessage> SaveMessageAsync(ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.MessageId))
        {
            message.MessageId = Guid.NewGuid().ToString("N");
        }
        if (message.CreatedAt == default)
        {
            message.CreatedAt = DateTime.UtcNow;
        }

        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                INSERT INTO chat.Messages (
                    MessageId, ConversationId, SenderUserId, SenderName, SenderAvatar,
                    MessageText, MessageType, AttachmentUrl, FileName, FileSize, MetadataJson, Status, CreatedAt
                )
                VALUES (
                    @MessageId, @ConversationId, @SenderUserId, @SenderName, @SenderAvatar,
                    @MessageText, @MessageType, @AttachmentUrl, @FileName, @FileSize, @MetadataJson, @Status, @CreatedAt
                );

                UPDATE chat.Conversations
                SET UpdatedAt = @CreatedAt
                WHERE ConversationId = @ConversationId;
            ";

            await conn.ExecuteAsync(sql, message);
        }
        catch
        {
            // Store in in-memory list
        }

        var list = _memoryMessages.GetOrAdd(message.ConversationId, _ => new List<ChatMessage>());
        lock (list)
        {
            list.Add(message);
        }

        if (_memoryConversations.TryGetValue(message.ConversationId, out var conv))
        {
            conv.LastMessage = message.MessageText;
            conv.LastMessageTime = message.CreatedAt;
        }

        return message;
    }

    public async Task<Conversation> CreateGroupAsync(long creatorUserId, CreateGroupDto dto)
    {
        var convId = "grp_" + Guid.NewGuid().ToString("N")[..8];
        var conv = new Conversation
        {
            ConversationId = convId,
            IsGroup = true,
            Title = dto.Name,
            Description = dto.Description,
            AvatarUrl = dto.AvatarUrl ?? "https://images.unsplash.com/photo-1558441719-7d0840db04b9?auto=format&fit=crop&w=150&q=80",
            ScopeRadiusKm = dto.RadiusKm,
            Category = dto.Category ?? "Convoy",
            CreatedByUserId = creatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ParticipantCount = 5,
            LastMessage = "Group created",
            LastMessageTime = DateTime.UtcNow,
            IsOnline = true
        };

        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                INSERT INTO chat.Conversations (
                    ConversationId, IsGroup, Title, Description, AvatarUrl, ScopeRadiusKm, Category, CreatedByUserId, CreatedAt, UpdatedAt
                )
                VALUES (
                    @ConversationId, @IsGroup, @Title, @Description, @AvatarUrl, @ScopeRadiusKm, @Category, @CreatedByUserId, @CreatedAt, @UpdatedAt
                );

                INSERT INTO chat.ConversationParticipants (ConversationId, UserId, JoinedAt, IsAdmin)
                VALUES (@ConversationId, @CreatedByUserId, @CreatedAt, 1);
            ";

            await conn.ExecuteAsync(sql, conv);
        }
        catch
        {
            // Ignore DB error, store in memory
        }

        _memoryConversations[convId] = conv;
        _memoryMessages[convId] = new List<ChatMessage>();

        return conv;
    }

    public async Task<Conversation?> UpdateGroupAsync(string conversationId, UpdateGroupDto dto)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                UPDATE chat.Conversations
                SET Title = COALESCE(@Title, Title),
                    Description = COALESCE(@Description, Description),
                    AvatarUrl = COALESCE(@AvatarUrl, AvatarUrl),
                    ScopeRadiusKm = COALESCE(@RadiusKm, ScopeRadiusKm),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ConversationId = @ConversationId AND IsGroup = 1;
            ";
            await conn.ExecuteAsync(sql, new { conversationId, dto.Title, dto.Description, dto.AvatarUrl, dto.RadiusKm });
        }
        catch { }

        if (_memoryConversations.TryGetValue(conversationId, out var conv))
        {
            if (!string.IsNullOrEmpty(dto.Title)) conv.Title = dto.Title;
            if (!string.IsNullOrEmpty(dto.Description)) conv.Description = dto.Description;
            if (!string.IsNullOrEmpty(dto.AvatarUrl)) conv.AvatarUrl = dto.AvatarUrl;
            if (dto.RadiusKm.HasValue) conv.ScopeRadiusKm = dto.RadiusKm.Value;
            conv.UpdatedAt = DateTime.UtcNow;
            return conv;
        }

        return null;
    }

    public async Task<bool> UpdateMemberRoleAsync(string conversationId, long userId, bool isAdmin)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                UPDATE chat.ConversationParticipants
                SET IsAdmin = @IsAdmin
                WHERE ConversationId = @ConversationId AND UserId = @UserId;
            ";
            await conn.ExecuteAsync(sql, new { conversationId, userId, isAdmin });
        }
        catch { }

        if (_memoryConversations.TryGetValue(conversationId, out var conv))
        {
            var member = conv.Members.FirstOrDefault(m => m.UserId == userId);
            if (member != null) member.IsAdmin = isAdmin;
            return true;
        }
        return true;
    }

    public async Task<bool> RemoveMemberAsync(string conversationId, long userId)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                DELETE FROM chat.ConversationParticipants
                WHERE ConversationId = @ConversationId AND UserId = @UserId;
            ";
            await conn.ExecuteAsync(sql, new { conversationId, userId });
        }
        catch { }

        if (_memoryConversations.TryGetValue(conversationId, out var conv))
        {
            conv.Members.RemoveAll(m => m.UserId == userId);
            conv.ParticipantCount = Math.Max(1, conv.ParticipantCount - 1);
            return true;
        }
        return true;
    }

    public async Task<bool> AddMemberAsync(string conversationId, long userId, bool isAdmin)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM chat.ConversationParticipants WHERE ConversationId = @ConversationId AND UserId = @UserId)
                BEGIN
                    INSERT INTO chat.ConversationParticipants (ConversationId, UserId, JoinedAt, IsAdmin)
                    VALUES (@ConversationId, @UserId, SYSUTCDATETIME(), @IsAdmin);
                END
            ";
            await conn.ExecuteAsync(sql, new { conversationId, userId, isAdmin });
        }
        catch { }

        if (_memoryConversations.TryGetValue(conversationId, out var conv))
        {
            if (!conv.Members.Any(m => m.UserId == userId))
            {
                conv.Members.Add(new GroupMemberDto
                {
                    UserId = userId,
                    Name = $"Driver #{userId}",
                    IsAdmin = isAdmin,
                    JoinedAt = DateTime.UtcNow
                });
                conv.ParticipantCount++;
            }
            return true;
        }
        return true;
    }

    public async Task<IEnumerable<NearbyUserDto>> GetNearbyUsersAsync(double myLat, double myLng, double radiusKm = 100)
    {
        var result = new List<NearbyUserDto>();

        // Realistic seed nearby EV drivers within 100km radius
        var mockNearbyPool = new List<NearbyUserDto>
        {
            new() { UserId = 101, Name = "Rajesh Kannan", Email = "rajesh.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80", CarModel = "Tata Tiago EV XZ+", Plate = "TN 38 BK 4210", Soc = 74, Latitude = 11.0255, Longitude = 76.9625, Status = "Charging", CurrentStation = "Zeon 60kW Avinashi Hub" },
            new() { UserId = 102, Name = "Dr. Sarah Thomas", Email = "sarah.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=150&q=80", CarModel = "MG ZS EV Exclusive", Plate = "TN 37 EV 1990", Soc = 86, Latitude = 11.0489, Longitude = 76.9850, Status = "Online" },
            new() { UserId = 103, Name = "Vikramaditya", Email = "vikram.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=150&q=80", CarModel = "Hyundai Ioniq 5 AWD", Plate = "KL 07 EV 8821", Soc = 45, Latitude = 11.0920, Longitude = 77.0210, Status = "Driving" },
            new() { UserId = 104, Name = "Priya Sharma", Email = "priya.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=150&q=80", CarModel = "BYD Atto 3 Superior", Plate = "TN 39 BC 7701", Soc = 91, Latitude = 11.1850, Longitude = 77.1200, Status = "Online" },
            new() { UserId = 105, Name = "Karthik EV", Email = "karthik.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?auto=format&fit=crop&w=150&q=80", CarModel = "Mahindra XUV400 EL Pro", Plate = "TN 33 DA 5566", Soc = 38, Latitude = 11.3400, Longitude = 77.2900, Status = "Charging", CurrentStation = "Tata Power 50kW Erode" },
            new() { UserId = 106, Name = "Anand Mohan", Email = "anand.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?auto=format&fit=crop&w=150&q=80", CarModel = "Tata Nexon EV Max", Plate = "TN 43 EV 2024", Soc = 62, Latitude = 11.4100, Longitude = 76.7000, Status = "Online" },
            new() { UserId = 107, Name = "Suresh Nair", Email = "suresh.ev@gmail.com", AvatarUrl = "https://images.unsplash.com/photo-1506794778202-cad84cf45f1d?auto=format&fit=crop&w=150&q=80", CarModel = "Kia EV6 GT-Line", Plate = "KL 09 EV 9900", Soc = 79, Latitude = 10.7867, Longitude = 76.6548, Status = "Idle" }
        };

        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                SELECT 
                    u.UserId,
                    ISNULL(u.FirstName + ' ' + u.LastName, u.Email) AS Name,
                    u.Email,
                    u.ProfileImageUrl AS AvatarUrl,
                    ISNULL(u.Latitude, 11.0168) AS Latitude,
                    ISNULL(u.Longitude, 76.9558) AS Longitude,
                    ISNULL(u.LastActiveAt, SYSUTCDATETIME()) AS LastActiveAt,
                    ISNULL(v.RegistrationNumber, 'TN 38 EV 8903') AS Plate,
                    ISNULL(vm.ModelName, 'Tata Nexon EV') AS CarModel,
                    82 AS Soc
                FROM [identity].[Users] u
                LEFT JOIN [vehicle].[Vehicles] v ON v.UserId = u.UserId
                LEFT JOIN [vehicle].[VehicleVariants] vv ON vv.VariantId = v.VariantId
                LEFT JOIN [vehicle].[VehicleModels] vm ON vm.ModelId = vv.ModelId;
            ";

            var dbUsers = (await conn.QueryAsync<dynamic>(sql)).ToList();
            if (dbUsers.Any())
            {
                foreach (var dbU in dbUsers)
                {
                    double uLat = Convert.ToDouble(dbU.Latitude ?? 11.0168);
                    double uLng = Convert.ToDouble(dbU.Longitude ?? 76.9558);
                    var dist = CalculateHaversineDistance(myLat, myLng, uLat, uLng);
                    if (dist <= radiusKm)
                    {
                        result.Add(new NearbyUserDto
                        {
                            UserId = Convert.ToInt64(dbU.UserId),
                            Name = (string)(dbU.Name ?? "EV Driver"),
                            Email = (string)(dbU.Email ?? ""),
                            AvatarUrl = (string)(dbU.AvatarUrl ?? "https://lh3.googleusercontent.com/a/default-user"),
                            CarModel = (string)(dbU.CarModel ?? "Tata Nexon EV"),
                            Plate = (string)(dbU.Plate ?? "TN 38 EV 8903"),
                            Soc = 85,
                            Latitude = uLat,
                            Longitude = uLng,
                            DistanceKm = Math.Round(dist, 1),
                            Status = "Online"
                        });
                    }
                }
                return result.OrderBy(u => u.DistanceKm);
            }
        }
        catch
        {
            // Fallback to calculation on mockNearbyPool
        }

        foreach (var u in mockNearbyPool)
        {
            var dist = CalculateHaversineDistance(myLat, myLng, u.Latitude, u.Longitude);
            if (dist <= radiusKm)
            {
                u.DistanceKm = Math.Round(dist, 1);
                result.Add(u);
            }
        }

        return result.OrderBy(u => u.DistanceKm);
    }

    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371; // Earth radius in km
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}
