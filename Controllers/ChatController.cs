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

    public ChatController(
        IChatRepository chatRepo,
        IHubContext<ChatHub> hubContext,
        IWebHostEnvironment env,
        ILogger<ChatController> logger,
        ConnectBackend.Services.IPushService push)
    {
        _chatRepo = chatRepo;
        _hubContext = hubContext;
        _env = env;
        _logger = logger;
        _push = push;
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

        // Broadcast in real-time via SignalR
        await _hubContext.Clients.Group(dto.ConversationId).SendAsync("ReceiveMessage", saved);

        // Push notification to the other person in a direct chat (id = chat_{a}_{b})
        var idParts = dto.ConversationId.Split('_');
        if (idParts.Length == 3 && idParts[0] == "chat" &&
            long.TryParse(idParts[1], out var ua) && long.TryParse(idParts[2], out var ub))
        {
            var receiverId = ua == userId ? ub : ua;
            var preview = string.IsNullOrWhiteSpace(dto.MessageText) ? "Sent an attachment" : dto.MessageText;
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
        return Ok(new { success = true, message = "Location updated successfully." });
    }

    [HttpPost("location-sharing")]
    public async Task<IActionResult> SetLocationSharing([FromBody] LocationSharingRequest req)
    {
        var success = await _chatRepo.SetLocationSharingAsync(req.UserId, req.IsDiscoverable);
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

