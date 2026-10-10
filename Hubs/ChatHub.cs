using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ConnectBackend.Hubs;

public class ChatHub : Hub
{
    private readonly IChatRepository _chatRepo;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IChatRepository chatRepo, ILogger<ChatHub> logger)
    {
        _chatRepo = chatRepo;
        _logger = logger;
    }

    public async Task JoinConversation(string conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
        _logger.LogInformation("Connection {ConnectionId} joined conversation {ConversationId}", Context.ConnectionId, conversationId);
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);
    }

    public async Task SendRealTimeMessage(SendMessageDto dto, string senderName, string? senderAvatar, long senderUserId)
    {
        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = dto.ConversationId,
            SenderUserId = senderUserId,
            SenderName = senderName,
            SenderAvatar = senderAvatar,
            MessageText = dto.MessageText,
            MessageType = dto.MessageType,
            AttachmentUrl = dto.AttachmentUrl,
            FileName = dto.FileName,
            FileSize = dto.FileSize,
            MetadataJson = dto.MetadataJson,
            Status = "delivered",
            CreatedAt = DateTime.UtcNow
        };

        // Persist in SQL Server
        await _chatRepo.SaveMessageAsync(message);

        // Broadcast to group members in real-time
        await Clients.Group(dto.ConversationId).SendAsync("ReceiveMessage", message);
    }

    public async Task SendTypingIndicator(string conversationId, string userName, bool isTyping)
    {
        await Clients.OthersInGroup(conversationId).SendAsync("UserTyping", new TypingNotificationDto
        {
            ConversationId = conversationId,
            UserName = userName,
            IsTyping = isTyping
        });
    }

    public async Task BroadcastSosAlert(string alertText, double lat, double lng, string userName, string vehicleInfo)
    {
        var sosPayload = new
        {
            AlertText = alertText,
            Latitude = lat,
            Longitude = lng,
            UserName = userName,
            VehicleInfo = vehicleInfo,
            Timestamp = DateTime.UtcNow
        };

        // Broadcast to all connected clients within community
        await Clients.All.SendAsync("SosBroadcast", sosPayload);
    }
}
