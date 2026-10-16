using System.Collections.Concurrent;
using System.Data;
using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using ConnectBackend.Services;
using Dapper;

namespace ConnectBackend.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly IDbConnectionFactory _dbFactory;

    // In-memory cache synced with registered users when running in cloud containers without external MSSQL
    private static readonly ConcurrentDictionary<string, Conversation> _memoryConversations = new();
    private static readonly ConcurrentDictionary<string, List<ChatMessage>> _memoryMessages = new();
    private static readonly ConcurrentDictionary<long, NearbyUserDto> _registeredUsers = new();



    private readonly IBigQueryService _bqService;

    public ChatRepository(IDbConnectionFactory dbFactory, IBigQueryService bqService)
    {
        _dbFactory = dbFactory;
        _bqService = bqService;
    }

    public async Task<IEnumerable<Conversation>> GetConversationsAsync(long userId)
    {
        if (_bqService.IsConfigured)
        {
            var bqConvs = await _bqService.GetConversationsAsync(userId);
            if (bqConvs.Any()) return bqConvs;
        }

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

            var list = (await conn.QueryAsync<Conversation>(sql)).ToList();
            if (list.Any()) return list;
        }
        catch
        {
            // Fallback to memory
        }

        return _memoryConversations.Values.OrderByDescending(c => c.LastMessageTime ?? c.CreatedAt);
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 50)
    {
        if (_bqService.IsConfigured)
        {
            var bqMsgs = await _bqService.GetMessagesAsync(conversationId, limit);
            if (bqMsgs.Any()) return bqMsgs;
        }

        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                SELECT * FROM (
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
                    ORDER BY CreatedAt DESC
                ) sub
                ORDER BY sub.CreatedAt ASC;
            ";

            var list = (await conn.QueryAsync<ChatMessage>(sql, new { ConversationId = conversationId, Limit = limit })).ToList();
            if (list.Any()) return list;
        }
        catch
        {
            // Fallback to memory
        }

        if (_memoryMessages.TryGetValue(conversationId, out var msgs))
        {
            return msgs.OrderBy(m => m.CreatedAt).TakeLast(limit);
        }

        return Enumerable.Empty<ChatMessage>();
    }

    public async Task<ChatMessage> SaveMessageAsync(ChatMessage message)
    {
        message.MessageId ??= Guid.NewGuid().ToString("N");
        message.CreatedAt = DateTime.UtcNow;

        if (_bqService.IsConfigured)
        {
            await _bqService.InsertChatMessageAsync(message);
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
            // Fallback to memory
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
            conv.UpdatedAt = message.CreatedAt;
        }

        return message;
    }

    public async Task<Conversation> CreateGroupAsync(long creatorUserId, CreateGroupDto dto)
    {
        var convId = $"grp_{Guid.NewGuid():N}"[..12];
        var conv = new Conversation
        {
            ConversationId = convId,
            IsGroup = true,
            Title = dto.Name,
            Description = dto.Description ?? "",
            AvatarUrl = dto.AvatarUrl ?? "https://images.unsplash.com/photo-1558441719-7d0840db04b9?auto=format&fit=crop&w=150&q=80",
            ScopeRadiusKm = dto.RadiusKm > 0 ? dto.RadiusKm : 100,
            Category = dto.Category ?? "Convoy",
            CreatedByUserId = creatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ParticipantCount = 1,
            LastMessage = "Group created",
            LastMessageTime = DateTime.UtcNow,
            IsOnline = true
        };

        if (_bqService.IsConfigured)
        {
            await _bqService.InsertConversationAsync(conv);
        }

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
        catch { }

        _memoryConversations[convId] = conv;
        _memoryMessages[convId] = new List<ChatMessage>();

        return conv;
    }

    public async Task<Conversation?> UpdateGroupAsync(string conversationId, UpdateGroupDto dto)
    {
        if (_bqService.IsConfigured)
        {
            var bqGroup = await _bqService.UpdateGroupAsync(conversationId, dto);
            if (bqGroup != null)
            {
                _memoryConversations[conversationId] = bqGroup;
                return bqGroup;
            }
        }

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
        if (_bqService.IsConfigured)
        {
            await _bqService.UpdateMemberRoleAsync(conversationId, userId, isAdmin);
        }

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
        if (_bqService.IsConfigured)
        {
            await _bqService.RemoveMemberAsync(conversationId, userId);
        }

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
            conv.ParticipantCount = Math.Max(0, conv.ParticipantCount - 1);
            return true;
        }
        return true;
    }

    public async Task<bool> AddMemberAsync(string conversationId, long userId, bool isAdmin)
    {
        if (_bqService.IsConfigured)
        {
            await _bqService.AddMemberAsync(conversationId, userId, isAdmin);
        }

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

    public async Task<bool> UpdateUserLocationAsync(long userId, double lat, double lng, bool isDiscoverable)
    {
        if (_bqService.IsConfigured)
        {
            await _bqService.UpdateUserLocationAsync(userId, lat, lng, isDiscoverable);
        }

        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                UPDATE [identity].[Users]
                SET Latitude = @lat,
                    Longitude = @lng,
                    IsDiscoverable = @isDiscoverable,
                    LastActiveAt = SYSUTCDATETIME()
                WHERE UserId = @userId;
            ";
            await conn.ExecuteAsync(sql, new { userId, lat, lng, isDiscoverable });
        }
        catch { }

        if (_registeredUsers.TryGetValue(userId, out var user))
        {
            user.Latitude = lat;
            user.Longitude = lng;
            user.Status = "Online";
        }
        else
        {
            _registeredUsers[userId] = new NearbyUserDto
            {
                UserId = userId,
                Name = $"User #{userId}",
                Email = "",
                AvatarUrl = "https://lh3.googleusercontent.com/a/default-user",
                CarModel = "Electric Vehicle",
                Plate = "TN 38 EV 0000",
                Soc = 85,
                Latitude = lat,
                Longitude = lng,
                Status = "Online"
            };
        }

        return true;
    }

    public async Task<bool> SetLocationSharingAsync(long userId, bool isDiscoverable)
    {
        if (_bqService.IsConfigured)
        {
            await _bqService.SetDiscoverableAsync(userId, isDiscoverable);
        }

        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                UPDATE [identity].[Users]
                SET IsDiscoverable = @isDiscoverable,
                    LastActiveAt = SYSUTCDATETIME()
                WHERE UserId = @userId;
            ";
            await conn.ExecuteAsync(sql, new { userId, isDiscoverable });
        }
        catch { }

        return true;
    }

    public async Task<IEnumerable<NearbyUserDto>> GetNearbyUsersAsync(double myLat, double myLng, double radiusKm = 100)
    {
        if (_bqService.IsConfigured)
        {
            return await _bqService.GetNearbyUsersAsync(myLat, myLng, radiusKm);
        }

        var result = new List<NearbyUserDto>();

        try
        {
            using var conn = _dbFactory.CreateConnection();

            const string sql = @"
                SELECT 
                    u.UserId,
                    ISNULL(u.FirstName + ' ' + u.LastName, u.Email) AS Name,
                    u.Email,
                    u.ProfileImageUrl AS AvatarUrl,
                    u.Latitude,
                    u.Longitude,
                    u.LastActiveAt,
                    ISNULL(v.RegistrationNumber, 'TN 38 EV 8903') AS Plate,
                    ISNULL(vm.ModelName, 'Tata Nexon EV') AS CarModel,
                    88 AS Soc
                FROM [identity].[Users] u
                LEFT JOIN [vehicle].[Vehicles] v ON v.UserId = u.UserId
                LEFT JOIN [vehicle].[VehicleVariants] vv ON vv.VariantId = v.VariantId
                LEFT JOIN [vehicle].[VehicleModels] vm ON vm.ModelId = vv.ModelId
                WHERE ISNULL(u.IsDiscoverable, 1) = 1 AND u.Latitude IS NOT NULL AND u.Longitude IS NOT NULL;
            ";

            var dbUsers = (await conn.QueryAsync<dynamic>(sql)).ToList();
            if (dbUsers.Any())
            {
                foreach (var dbU in dbUsers)
                {
                    double uLat = Convert.ToDouble(dbU.Latitude);
                    double uLng = Convert.ToDouble(dbU.Longitude);
                    var dist = CalculateHaversineDistance(myLat, myLng, uLat, uLng);
                    if (dist <= radiusKm)
                    {
                        result.Add(new NearbyUserDto
                        {
                            UserId = Convert.ToInt64(dbU.UserId),
                            Name = (string)(dbU.Name ?? "EV Driver"),
                            Email = (string)(dbU.Email ?? ""),
                            AvatarUrl = (string)(dbU.AvatarUrl ?? "https://lh3.googleusercontent.com/a/default-user"),
                            CarModel = (string)(dbU.CarModel ?? "Electric Vehicle"),
                            Plate = (string)(dbU.Plate ?? "TN 38 EV 8903"),
                            Soc = 85,
                            // Privacy safeguard: do not expose raw exact micro coordinates
                            Latitude = Math.Round(uLat, 2),
                            Longitude = Math.Round(uLng, 2),
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
            // Fallback to registered users in memory
        }

        foreach (var u in _registeredUsers.Values)
        {
            var dist = CalculateHaversineDistance(myLat, myLng, u.Latitude, u.Longitude);
            if (dist <= radiusKm)
            {
                result.Add(new NearbyUserDto
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Email,
                    AvatarUrl = u.AvatarUrl,
                    CarModel = u.CarModel,
                    Plate = u.Plate,
                    Soc = u.Soc,
                    Latitude = Math.Round(u.Latitude, 2),
                    Longitude = Math.Round(u.Longitude, 2),
                    DistanceKm = Math.Round(dist, 1),
                    Status = u.Status
                });
            }
        }

        return result.OrderBy(u => u.DistanceKm);
    }

    public async Task<IEnumerable<NearbyUserDto>> GetContactsAsync()
    {
        if (_bqService.IsConfigured)
        {
            var bqContacts = await _bqService.GetContactsAsync();
            if (bqContacts.Any()) return bqContacts;
        }

        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                SELECT 
                    u.UserId,
                    ISNULL(u.FirstName + ' ' + u.LastName, u.Email) AS Name,
                    u.Email,
                    u.MobileNumber AS Plate,
                    u.ProfileImageUrl AS AvatarUrl,
                    ISNULL(vm.ModelName, 'Electric Vehicle') AS CarModel,
                    'Online' AS Status
                FROM [identity].[Users] u
                LEFT JOIN [vehicle].[Vehicles] v ON v.UserId = u.UserId
                LEFT JOIN [vehicle].[VehicleVariants] vv ON vv.VariantId = v.VariantId
                LEFT JOIN [vehicle].[VehicleModels] vm ON vm.ModelId = vv.ModelId;
            ";

            var dbUsers = (await conn.QueryAsync<NearbyUserDto>(sql)).ToList();
            if (dbUsers.Any()) return dbUsers;
        }
        catch { }

        return _registeredUsers.Values;
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
