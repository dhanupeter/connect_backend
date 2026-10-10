namespace ConnectBackend.Models;

public class Conversation
{
    public string ConversationId { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public int ScopeRadiusKm { get; set; } = 100;
    public string? Category { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Computed / Joined fields
    public string? LastMessage { get; set; }
    public DateTime? LastMessageTime { get; set; }
    public int UnreadCount { get; set; }
    public int ParticipantCount { get; set; }
    public double? DistanceKm { get; set; }
    public string? CarModel { get; set; }
    public string? Plate { get; set; }
    public int? Soc { get; set; }
    public bool IsOnline { get; set; } = true;
    public List<GroupMemberDto> Members { get; set; } = new();
}

public class ChatMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = string.Empty;
    public long SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text"; // text, image, file, location, station, sos
    public string? AttachmentUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MetadataJson { get; set; }
    public string Status { get; set; } = "delivered"; // sent, delivered, read
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SendMessageDto
{
    public string ConversationId { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text";
    public string? AttachmentUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MetadataJson { get; set; }
}

public class CreateGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int RadiusKm { get; set; } = 100;
    public string? Category { get; set; } = "Convoy";
    public string? AvatarUrl { get; set; }
}

public class UpdateGroupDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public int? RadiusKm { get; set; }
}

public class GroupMemberDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Plate { get; set; }
    public string? CarModel { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class CallSignalDto
{
    public string ChannelId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string CallType { get; set; } = "voice"; // "voice" or "video"
    public string? Sdp { get; set; }
    public object? IceCandidate { get; set; }
}

public class NearbyUserDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string CarModel { get; set; } = "Tata Nexon EV";
    public string Plate { get; set; } = "TN 38 EV 8903";
    public int Soc { get; set; } = 84;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double DistanceKm { get; set; }
    public string Status { get; set; } = "Online";
    public string? CurrentStation { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class TypingNotificationDto
{
    public string ConversationId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsTyping { get; set; }
}
