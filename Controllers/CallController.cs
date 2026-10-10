using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

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
    public string Status { get; set; } = "ringing"; // "ringing", "accepted", "rejected", "ended"
    public string? OfferSdp { get; set; }
    public string? AnswerSdp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<IceCandidateDto> CandidatesFromCaller { get; set; } = new();
    public List<IceCandidateDto> CandidatesFromCallee { get; set; } = new();
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

    [HttpPost("invite")]
    public IActionResult Invite([FromBody] CallInviteRequest req)
    {
        // Cancel any previous hanging calls for these users
        foreach (var kvp in _activeCalls)
        {
            if (kvp.Value.CallerId == req.CallerId || kvp.Value.CalleeId == req.CalleeId)
            {
                if ((DateTime.UtcNow - kvp.Value.CreatedAt).TotalSeconds > 120)
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
            CallType = req.CallType,
            OfferSdp = req.OfferSdp,
            Status = "ringing",
            CreatedAt = DateTime.UtcNow
        };

        _activeCalls[session.CallId] = session;
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
    public IActionResult Answer([FromBody] CallAnswerRequest req)
    {
        if (_activeCalls.TryGetValue(req.CallId, out var session))
        {
            session.AnswerSdp = req.AnswerSdp;
            session.Status = "accepted";
            return Ok(new { success = true, data = session });
        }

        return NotFound(new { success = false, message = "Call session not found." });
    }

    [HttpPost("reject")]
    public IActionResult Reject([FromBody] CallAnswerRequest req)
    {
        if (_activeCalls.TryGetValue(req.CallId, out var session))
        {
            session.Status = "rejected";
            return Ok(new { success = true });
        }

        return NotFound(new { success = false });
    }

    [HttpGet("status/{callId}")]
    public IActionResult GetStatus(string callId)
    {
        if (_activeCalls.TryGetValue(callId, out var session))
        {
            return Ok(new { success = true, data = session });
        }

        return Ok(new { success = true, data = new { status = "ended" } });
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
        if (_activeCalls.TryGetValue(callId, out var session))
        {
            // If caller is asking, return candidates from callee, and vice versa
            var list = forUserId == session.CallerId
                ? session.CandidatesFromCallee
                : session.CandidatesFromCaller;

            return Ok(new { success = true, candidates = list });
        }

        return Ok(new { success = true, candidates = new List<IceCandidateDto>() });
    }

    [HttpPost("end")]
    public IActionResult EndCall([FromBody] CallAnswerRequest req)
    {
        if (_activeCalls.TryRemove(req.CallId, out var session))
        {
            session.Status = "ended";
        }

        return Ok(new { success = true });
    }
}
