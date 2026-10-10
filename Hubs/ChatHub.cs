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

        // Persist in SQL Server / in-memory
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

        await Clients.All.SendAsync("SosBroadcast", sosPayload);
    }

    // =========================================================================
    // WebRTC Calling & Media Signaling (Pure Open-Source - No Agora Required)
    // =========================================================================

    public async Task InitiateCall(string channelId, string callType, string callerName, string? callerAvatar, string? calleeUserId = null)
    {
        _logger.LogInformation("Initiating {CallType} call in channel {ChannelId} from {CallerName}", callType, channelId, callerName);
        
        var callPayload = new
        {
            ChannelId = channelId,
            CallType = callType, // "voice" or "video"
            CallerName = callerName,
            CallerAvatar = callerAvatar,
            CallerConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        };

        // Broadcast incoming call event to target channel or all participants
        await Clients.OthersInGroup(channelId).SendAsync("IncomingCall", callPayload);
    }

    public async Task AcceptCall(string channelId, string responderName, string callerConnectionId)
    {
        await Clients.Client(callerConnectionId).SendAsync("CallAccepted", new
        {
            ChannelId = channelId,
            ResponderName = responderName,
            ResponderConnectionId = Context.ConnectionId
        });
    }

    public async Task RejectCall(string channelId, string callerConnectionId, string reason)
    {
        await Clients.Client(callerConnectionId).SendAsync("CallRejected", new
        {
            ChannelId = channelId,
            Reason = reason ?? "busy",
            ResponderConnectionId = Context.ConnectionId
        });
    }

    public async Task EndCall(string channelId)
    {
        await Clients.Group(channelId).SendAsync("CallEnded", new
        {
            ChannelId = channelId,
            EndedBy = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task SendWebRtcSignal(string channelId, string targetConnectionId, object signalData)
    {
        // Relays SDP Offer, SDP Answer, or ICE candidate peer-to-peer
        if (!string.IsNullOrEmpty(targetConnectionId))
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveWebRtcSignal", new
            {
                SenderConnectionId = Context.ConnectionId,
                Signal = signalData
            });
        }
        else
        {
            await Clients.OthersInGroup(channelId).SendAsync("ReceiveWebRtcSignal", new
            {
                SenderConnectionId = Context.ConnectionId,
                Signal = signalData
            });
        }
    }
}
