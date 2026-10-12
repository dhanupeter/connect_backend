using System.Collections.Concurrent;
using System.Data;
using ConnectBackend.Data;
using ConnectBackend.Hubs;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConnectBackend.Controllers;

public class CallSession
{
    public string CallId { get; set; } = Guid.NewGuid().ToString("N");
    public string CallType { get; set; } = "voice"; // "voice" or "video"
    public long CallerId { get; set; }
    public string CallerName { get; set; } = "EV Driver";
    public string CallerCar { get; set; } = "Electric Vehicle";
    public string CallerAvatar { get; set; } = "https://lh3.googleusercontent.com/a/default-user";
    public long CalleeId { get; set; }
    public string CalleeName { get; set; } = "EV Driver";
    public string CalleeAvatar { get; set; } = "https://lh3.googleusercontent.com/a/default-user";
    public string Status { get; set; } = "ringing"; // "ringing", "accepted", "rejected", "ended"
    public string? OfferSdp { get; set; }
    public string? AnswerSdp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConnectedAt { get; set; }
    public List<IceCandidateDto> CandidatesFromCaller { get; set; } = new();
    public List<IceCandidateDto> CandidatesFromCallee { get; set; } = new();
}

public class CallLogRecord
{
    public string CallId { get; set; } = Guid.NewGuid().ToString("N");
    public long CallerId { get; set; }
    public string CallerName { get; set; } = "EV Driver";
    public string? CallerAvatar { get; set; }
    public string? CallerCar { get; set; }
    public long CalleeId { get; set; }
    public string? CalleeName { get; set; }
    public string? CalleeAvatar { get; set; }
    public string CallType { get; set; } = "voice"; // "voice" or "video"
    public string Status { get; set; } = "missed"; // "incoming", "outgoing", "missed", "connected", "ended"
    public int DurationSeconds { get; set; } = 0;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}

public class IceCandidateDto
{
    public string Candidate { get; set; } = "";
    public string? SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}

public class CallInviteRequest
{
    public long CallerId { get; set; }
    public string CallerName { get; set; } = "EV Driver";
    public string CallerCar { get; set; } = "Electric Vehicle";
    public string CallerAvatar { get; set; } = "https://lh3.googleusercontent.com/a/default-user";
    public long CalleeId { get; set; }
    public string? CalleeName { get; set; }
    public string? CalleeAvatar { get; set; }
    public string CallType { get; set; } = "voice";
    public string? OfferSdp { get; set; }
}

public class CallAnswerRequest
{
    public string CallId { get; set; } = "";
    public string AnswerSdp { get; set; } = "";
}

public class AddCandidateRequest
{
    public string CallId { get; set; } = "";
    public long FromUserId { get; set; }
    public string Candidate { get; set; } = "";
    public string? SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class CallController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, CallSession> _activeCalls = new();
    private static readonly ConcurrentDictionary<string, CallLogRecord> _callHistory = new();
    private static readonly ConcurrentDictionary<string, DateTime> _endedCallIds = new();
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ILogger<CallController> _logger;
    private readonly ConnectBackend.Services.IPushService _push;
    private readonly IHubContext<ChatHub> _hubContext;

    public CallController(
        IDbConnectionFactory dbFactory,
        ILogger<CallController> logger,
        ConnectBackend.Services.IPushService push,
        IHubContext<ChatHub> hubContext)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _push = push;
        _hubContext = hubContext;
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] CallInviteRequest req)
    {
        // Cancel any lingering sessions for these users
        foreach (var kvp in _activeCalls)
        {
            if (kvp.Value.CallerId == req.CallerId || kvp.Value.CalleeId == req.CalleeId)
            {
                if ((DateTime.UtcNow - kvp.Value.CreatedAt).TotalSeconds > 90)
                {
                    _activeCalls.TryRemove(kvp.Key, out _);
                }
            }
        }

        var session = new CallSession
        {
            CallerId = req.CallerId,
            CallerName = req.CallerName,
            CallerCar = req.CallerCar,
            CallerAvatar = req.CallerAvatar,
            CalleeId = req.CalleeId,
            CalleeName = req.CalleeName ?? "EV Driver",
            CalleeAvatar = req.CalleeAvatar ?? "https://lh3.googleusercontent.com/a/default-user",
            CallType = req.CallType,
            OfferSdp = req.OfferSdp,
            Status = "ringing",
            CreatedAt = DateTime.UtcNow
        };

        _activeCalls[session.CallId] = session;

        // Log call record
        var log = new CallLogRecord
        {
            CallId = session.CallId,
            CallerId = session.CallerId,
            CallerName = session.CallerName,
            CallerAvatar = session.CallerAvatar,
            CallerCar = session.CallerCar,
            CalleeId = session.CalleeId,
            CalleeName = session.CalleeName,
            CalleeAvatar = session.CalleeAvatar,
            CallType = session.CallType,
            Status = "ringing",
            StartedAt = session.CreatedAt
        };
        _callHistory[session.CallId] = log;

        _ = _push.SendToUserAsync(
            session.CalleeId,
            $"Incoming {(session.CallType == "video" ? "video" : "voice")} call",
            $"{session.CallerName} is calling you",
            new Dictionary<string, string>
            {
                ["type"] = "call",
                ["callId"] = session.CallId,
                ["callType"] = session.CallType,
                ["callerId"] = session.CallerId.ToString(),
                ["callerName"] = session.CallerName
            },
            highPriority: true);

        await PersistCallLogAsync(log);

        return Ok(new { success = true, data = session });
    }

    [HttpGet("incoming/{userId}")]
    public IActionResult GetIncomingCall(long userId)
    {
        var incoming = _activeCalls.Values
            .Where(c => c.CalleeId == userId && c.Status == "ringing" && (DateTime.UtcNow - c.CreatedAt).TotalSeconds < 45)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (incoming == null)
        {
            return Ok(new { success = true, incomingCall = (object?)null });
        }

        return Ok(new { success = true, incomingCall = incoming });
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer([FromBody] CallAnswerRequest req)
    {
        if (_activeCalls.TryGetValue(req.CallId, out var session))
        {
            session.AnswerSdp = req.AnswerSdp;
            session.Status = "accepted";
            session.ConnectedAt = DateTime.UtcNow;

            if (_callHistory.TryGetValue(req.CallId, out var log))
            {
                log.Status = "connected";
                await PersistCallLogAsync(log);
            }

            return Ok(new { success = true, data = session });
        }

        return NotFound(new { success = false, message = "Call session not found." });
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] CallAnswerRequest req)
    {
        _endedCallIds[req.CallId] = DateTime.UtcNow;
        if (_activeCalls.TryGetValue(req.CallId, out var session))
        {
            session.Status = "rejected";

            if (_callHistory.TryGetValue(req.CallId, out var log))
            {
                log.Status = "missed";
                log.EndedAt = DateTime.UtcNow;
                await PersistCallLogAsync(log);
            }

            try
            {
                await _hubContext.Clients.All.SendAsync("CallEnded", req.CallId);
            }
            catch { }

            return Ok(new { success = true, callEnded = true, status = "rejected" });
        }

        return NotFound(new { success = false });
    }

    [HttpGet("status/{callId}")]
    public IActionResult GetStatus(string callId)
    {
        if (_endedCallIds.ContainsKey(callId))
        {
            return Ok(new { success = true, callEnded = true, data = new { status = "ended" } });
        }

        if (_activeCalls.TryGetValue(callId, out var session))
        {
            var isEnded = session.Status == "ended" || session.Status == "rejected";
            return Ok(new { success = true, callEnded = isEnded, data = session });
        }

        return Ok(new { success = true, callEnded = true, data = new { status = "ended" } });
    }

    [HttpPost("candidate")]
    public IActionResult AddCandidate([FromBody] AddCandidateRequest req)
    {
        if (_activeCalls.TryGetValue(req.CallId, out var session))
        {
            var cand = new IceCandidateDto
            {
                Candidate = req.Candidate,
                SdpMid = req.SdpMid,
                SdpMLineIndex = req.SdpMLineIndex
            };

            if (req.FromUserId == session.CallerId)
            {
                session.CandidatesFromCaller.Add(cand);
            }
            else
            {
                session.CandidatesFromCallee.Add(cand);
            }

            return Ok(new { success = true });
        }

        return NotFound(new { success = false });
    }

    [HttpGet("candidates/{callId}")]
    public IActionResult GetCandidates(string callId, [FromQuery] long forUserId)
    {
        if (_endedCallIds.ContainsKey(callId))
        {
            return Ok(new { success = true, callEnded = true, status = "ended", candidates = new List<IceCandidateDto>() });
        }

        if (_activeCalls.TryGetValue(callId, out var session))
        {
            if (session.Status == "ended" || session.Status == "rejected")
            {
                return Ok(new { success = true, callEnded = true, status = session.Status, candidates = new List<IceCandidateDto>() });
            }

            var list = forUserId == session.CallerId
                ? session.CandidatesFromCallee
                : session.CandidatesFromCaller;

            return Ok(new { success = true, callEnded = false, status = session.Status, candidates = list });
        }

        return Ok(new { success = true, callEnded = true, status = "ended", candidates = new List<IceCandidateDto>() });
    }

    [HttpPost("end")]
    public async Task<IActionResult> EndCall([FromBody] CallAnswerRequest req)
    {
        _endedCallIds[req.CallId] = DateTime.UtcNow;

        // Clean up stale ended IDs older than 10 minutes
        var staleKeys = _endedCallIds.Where(kvp => (DateTime.UtcNow - kvp.Value).TotalMinutes > 10).Select(kvp => kvp.Key).ToList();
        foreach (var k in staleKeys) _endedCallIds.TryRemove(k, out _);

        if (_activeCalls.TryRemove(req.CallId, out var session))
        {
            session.Status = "ended";

            if (_callHistory.TryGetValue(req.CallId, out var log))
            {
                log.EndedAt = DateTime.UtcNow;
                if (session.ConnectedAt.HasValue)
                {
                    log.DurationSeconds = (int)Math.Max(1, (DateTime.UtcNow - session.ConnectedAt.Value).TotalSeconds);
                    log.Status = "connected";
                }
                else
                {
                    log.Status = "missed";
                }
                await PersistCallLogAsync(log);
            }
        }

        try
        {
            await _hubContext.Clients.All.SendAsync("CallEnded", req.CallId);
        }
        catch { }

        return Ok(new { success = true, callEnded = true, status = "ended" });
    }

    [HttpGet("history/{userId}")]
    public async Task<IActionResult> GetCallHistory(long userId)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                SELECT 
                    CallId, CallerId, CallerName, CallerAvatar, CallerCar,
                    CalleeId, CalleeName, CalleeAvatar,
                    CallType, Status, DurationSeconds, StartedAt, EndedAt
                FROM [chat].[CallLogs]
                WHERE CallerId = @UserId OR CalleeId = @UserId
                ORDER BY StartedAt DESC;
            ";

            var rows = (await conn.QueryAsync<CallLogRecord>(sql, new { UserId = userId })).ToList();
            if (rows.Any())
            {
                var historyItems = rows.Select(r => FormatHistoryItem(r, userId)).ToList();
                return Ok(new { success = true, data = historyItems });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DB Call History query fallback: {Msg}", ex.Message);
        }

        // Memory fallback
        var memRows = _callHistory.Values
            .Where(r => r.CallerId == userId || r.CalleeId == userId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => FormatHistoryItem(r, userId))
            .ToList();

        return Ok(new { success = true, data = memRows });
    }

    private static object FormatHistoryItem(CallLogRecord r, long myUserId)
    {
        var isOutgoing = r.CallerId == myUserId;
        var contactId = isOutgoing ? r.CalleeId : r.CallerId;
        var contactName = isOutgoing ? (r.CalleeName ?? "EV Driver") : r.CallerName;
        var contactAvatar = isOutgoing ? (r.CalleeAvatar ?? "https://lh3.googleusercontent.com/a/default-user") : (r.CallerAvatar ?? "https://lh3.googleusercontent.com/a/default-user");
        var contactCar = isOutgoing ? "Electric Vehicle" : (r.CallerCar ?? "Electric Vehicle");

        return new
        {
            callId = r.CallId,
            contactId,
            contactName,
            contactAvatar,
            contactCar,
            callType = r.CallType,
            direction = isOutgoing ? "outgoing" : "incoming",
            status = r.Status,
            durationSeconds = r.DurationSeconds,
            startedAt = r.StartedAt.ToString("o")
        };
    }

    private async Task PersistCallLogAsync(CallLogRecord log)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                IF EXISTS (SELECT 1 FROM [chat].[CallLogs] WHERE CallId = @CallId)
                BEGIN
                    UPDATE [chat].[CallLogs]
                    SET Status = @Status,
                        DurationSeconds = @DurationSeconds,
                        EndedAt = @EndedAt
                    WHERE CallId = @CallId;
                END
                ELSE
                BEGIN
                    INSERT INTO [chat].[CallLogs] (
                        CallId, CallerId, CallerName, CallerAvatar, CallerCar,
                        CalleeId, CalleeName, CalleeAvatar,
                        CallType, Status, DurationSeconds, StartedAt, EndedAt
                    )
                    VALUES (
                        @CallId, @CallerId, @CallerName, @CallerAvatar, @CallerCar,
                        @CalleeId, @CalleeName, @CalleeAvatar,
                        @CallType, @Status, @DurationSeconds, @StartedAt, @EndedAt
                    );
                END
            ";
            await conn.ExecuteAsync(sql, log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Call log DB persist fallback: {Msg}", ex.Message);
        }
    }
}
