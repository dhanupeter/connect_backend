using ConnectBackend.Hubs;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatRepository _chatRepo;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ChatController> _logger;
    private readonly ConnectBackend.Services.IPushService _push;
    private readonly ConnectBackend.Services.IBigQueryService _bqService;

    public ChatController(
        IChatRepository chatRepo,
        IHubContext<ChatHub> hubContext,
        IWebHostEnvironment env,
        ILogger<ChatController> logger,
        ConnectBackend.Services.IPushService push,
        ConnectBackend.Services.IBigQueryService bqService)
    {
        _chatRepo = chatRepo;
        _hubContext = hubContext;
        _env = env;
        _logger = logger;
        _push = push;
        _bqService = bqService;
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] long userId = 4)
    {
        var list = await _chatRepo.GetConversationsAsync(userId);
        return Ok(new { success = true, data = list });
    }

    [HttpGet("messages/{conversationId}")]
    public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] int limit = 50)
    {
        var messages = await _chatRepo.GetMessagesAsync(conversationId, limit);
        return Ok(new { success = true, data = messages });
    }

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto, [FromQuery] long userId = 4, [FromQuery] string userName = "Dhanu Peter")
    {
        if (string.IsNullOrWhiteSpace(dto.MessageText) && string.IsNullOrWhiteSpace(dto.AttachmentUrl))
        {
            return BadRequest(new { success = false, message = "Message text or attachment is required." });
        }

        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = dto.ConversationId,
            SenderUserId = userId,
            SenderName = userName,
            SenderAvatar = "https://lh3.googleusercontent.com/a/default-user",
            MessageText = dto.MessageText,
            MessageType = dto.MessageType ?? "text",
            AttachmentUrl = dto.AttachmentUrl,
            FileName = dto.FileName,
            FileSize = dto.FileSize,
            MetadataJson = dto.MetadataJson,
            Status = "delivered",
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _chatRepo.SaveMessageAsync(message);

        // Broadcast in real-time via SignalR to conversation group
        await _hubContext.Clients.Group(dto.ConversationId).SendAsync("ReceiveMessage", saved);

        // Push notification & SignalR direct alert to the recipient in a direct chat (id = chat_{a}_{b})
        var idParts = dto.ConversationId.Split('_');
        if (idParts.Length == 3 && idParts[0] == "chat" &&
            long.TryParse(idParts[1], out var ua) && long.TryParse(idParts[2], out var ub))
        {
            var receiverId = ua == userId ? ub : ua;
            var preview = string.IsNullOrWhiteSpace(dto.MessageText) ? "Sent an attachment" : dto.MessageText;

            // SignalR user-level push event (immediately triggers heads-up notifications even when app is open or on other screens)
            try
            {
                await _hubContext.Clients.Group($"user_{receiverId}").SendAsync("NewMessageNotification", new
                {
                    ConversationId = dto.ConversationId,
                    SenderId = userId,
                    SenderName = userName,
                    MessageText = preview,
                    MessageType = dto.MessageType ?? "text",
                    CreatedAt = saved.CreatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Direct SignalR notification failed: {Msg}", ex.Message);
            }

            // Also send FCM push notification
            _ = _push.SendToUserAsync(receiverId, userName, preview, new Dictionary<string, string>
            {
                ["type"] = "chat",
                ["conversationId"] = dto.ConversationId,
                ["senderId"] = userId.ToString(),
                ["senderName"] = userName
            });
        }

        return Ok(new { success = true, data = saved });
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto, [FromQuery] long userId = 4)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { success = false, message = "Group name is required." });
        }

        var group = await _chatRepo.CreateGroupAsync(userId, dto);
        return Ok(new { success = true, data = group });
    }

    [HttpPut("groups/{id}")]
    public async Task<IActionResult> UpdateGroup(string id, [FromBody] UpdateGroupDto dto)
    {
        var updated = await _chatRepo.UpdateGroupAsync(id, dto);
        if (updated == null)
        {
            return NotFound(new { success = false, message = "Group not found." });
        }
        await _hubContext.Clients.Group(id).SendAsync("GroupDetailsUpdated", updated);
        return Ok(new { success = true, data = updated });
    }

    [HttpPut("groups/{id}/members/{memberId}/role")]
    public async Task<IActionResult> UpdateMemberRole(string id, long memberId, [FromBody] UpdateRoleRequest req)
    {
        var success = await _chatRepo.UpdateMemberRoleAsync(id, memberId, req.IsAdmin);
        await _hubContext.Clients.Group(id).SendAsync("MemberRoleUpdated", new { conversationId = id, userId = memberId, isAdmin = req.IsAdmin });
        return Ok(new { success = true, userId = memberId, isAdmin = req.IsAdmin });
    }

    [HttpDelete("groups/{id}/members/{memberId}")]
    public async Task<IActionResult> RemoveMember(string id, long memberId)
    {
        var success = await _chatRepo.RemoveMemberAsync(id, memberId);
        await _hubContext.Clients.Group(id).SendAsync("MemberRemoved", new { conversationId = id, userId = memberId });
        return Ok(new { success = true, userId = memberId });
    }

    [HttpPost("groups/{id}/members")]
    public async Task<IActionResult> AddMember(string id, [FromBody] AddMemberRequest req)
    {
        var success = await _chatRepo.AddMemberAsync(id, req.UserId, req.IsAdmin);
        await _hubContext.Clients.Group(id).SendAsync("MemberAdded", new { conversationId = id, userId = req.UserId, isAdmin = req.IsAdmin });
        return Ok(new { success = true, userId = req.UserId });
    }

    [HttpGet("contacts")]
    public async Task<IActionResult> GetContacts()
    {
        var contacts = await _chatRepo.GetContactsAsync();
        return Ok(new { success = true, count = contacts.Count(), data = contacts });
    }

    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest req)
    {
        var success = await _chatRepo.UpdateUserLocationAsync(req.UserId, req.Latitude, req.Longitude, req.IsDiscoverable);
        try
        {
            await _hubContext.Clients.All.SendAsync("NearbyLocationUpdated", new { userId = req.UserId, latitude = req.Latitude, longitude = req.Longitude, isDiscoverable = req.IsDiscoverable });
        }
        catch { }
        return Ok(new { success = true, message = "Location updated successfully." });
    }

    [HttpPost("location-sharing")]
    public async Task<IActionResult> SetLocationSharing([FromBody] LocationSharingRequest req)
    {
        var success = await _chatRepo.SetLocationSharingAsync(req.UserId, req.IsDiscoverable);
        try
        {
            await _hubContext.Clients.All.SendAsync("NearbyVisibilityChanged", new { userId = req.UserId, isDiscoverable = req.IsDiscoverable });
        }
        catch { }
        return Ok(new { success = true, isDiscoverable = req.IsDiscoverable });
    }

    [HttpGet("nearby")]
    public async Task<IActionResult> GetNearby(
        [FromQuery] double lat = 11.0168,
        [FromQuery] double lng = 76.9558,
        [FromQuery] double radiusKm = 100)
    {
        var nearbyUsers = await _chatRepo.GetNearbyUsersAsync(lat, lng, radiusKm);
        return Ok(new { success = true, radiusKm = radiusKm, count = nearbyUsers.Count(), data = nearbyUsers });
    }

    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)] // 50 MB limit
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "No file selected." });
        }

        try
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadDir = Path.Combine(webRoot, "uploads", "chat");

            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var extension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(file.FileName)}{extension}";
            var filePath = Path.Combine(uploadDir, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativeUrl = $"/uploads/chat/{uniqueFileName}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var fullUrl = $"{baseUrl}{relativeUrl}";

            var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            return Ok(new
            {
                success = true,
                fileUrl = fullUrl,
                relativeUrl = relativeUrl,
                fileName = file.FileName,
                fileSize = file.Length,
                mimeType = file.ContentType,
                isImage = isImage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chat file");
            return StatusCode(500, new { success = false, message = "File upload failed: " + ex.Message });
        }
    }

    [HttpPost("preferences")]
    public async Task<IActionResult> SetPreference([FromBody] ConversationPreferenceRequest req)
    {
        if (req.UserId <= 0 || string.IsNullOrWhiteSpace(req.ConversationId))
        {
            return BadRequest(new { success = false, message = "UserId and ConversationId are required." });
        }

        try
        {
            string safeCid = req.ConversationId.Replace("'", "''");
            string mutedVal = req.IsMuted.HasValue ? (req.IsMuted.Value ? "TRUE" : "FALSE") : "T.is_muted";
            string deletedVal = req.IsDeleted.HasValue ? (req.IsDeleted.Value ? "TRUE" : "FALSE") : "T.is_deleted";
            string blockedVal = req.IsBlocked.HasValue ? (req.IsBlocked.Value ? "TRUE" : "FALSE") : "T.is_blocked";
            string initMuted = req.IsMuted == true ? "TRUE" : "FALSE";
            string initDeleted = req.IsDeleted == true ? "TRUE" : "FALSE";
            string initBlocked = req.IsBlocked == true ? "TRUE" : "FALSE";

            var sql = $@"
                MERGE `event-506117.connectdb.user_preferences` T
                USING (SELECT {req.UserId} AS user_id, '{safeCid}' AS conversation_id) S
                ON T.user_id = S.user_id AND T.conversation_id = S.conversation_id
                WHEN MATCHED THEN
                    UPDATE SET 
                        is_muted = COALESCE({mutedVal}, FALSE),
                        is_deleted = COALESCE({deletedVal}, FALSE),
                        is_blocked = COALESCE({blockedVal}, FALSE),
                        updated_at = CURRENT_TIMESTAMP()
                WHEN NOT MATCHED THEN
                    INSERT (user_id, conversation_id, is_muted, is_deleted, is_blocked, updated_at)
                    VALUES ({req.UserId}, '{safeCid}', {initMuted}, {initDeleted}, {initBlocked}, CURRENT_TIMESTAMP());
            ";
            await _bqService.ExecuteQueryAsync(sql);
            return Ok(new { success = true, message = "Preferences saved successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set conversation preference");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}

public class ConversationPreferenceRequest
{
    public long UserId { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public bool? IsMuted { get; set; }
    public bool? IsDeleted { get; set; }
    public bool? IsBlocked { get; set; }
}

public class UpdateRoleRequest
{
    public bool IsAdmin { get; set; }
}

public class AddMemberRequest
{
    public long UserId { get; set; }
    public bool IsAdmin { get; set; }
}

public class UpdateLocationRequest
{
    public long UserId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsDiscoverable { get; set; } = true;
}

public class LocationSharingRequest
{
    public long UserId { get; set; }
    public bool IsDiscoverable { get; set; }
}

