using ConnectBackend.Dtos;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingRepository _bookingRepository;

    public BookingsController(IBookingRepository bookingRepository)
    {
        _bookingRepository = bookingRepository;
    }

    [HttpPost("hold")]
    public async Task<IActionResult> CreateHold([FromBody] CreateBookingRequest request)
    {
        if (!long.TryParse(request.UserId, out long uid)) uid = 1;
        if (!long.TryParse(request.StationId, out long stationId)) stationId = 1;

        string passcode = "EV-" + (100 + Random.Shared.Next(10, 999));

        var created = await _bookingRepository.CreateBookingHoldAsync(
            userId: uid,
            stationId: stationId,
            connectorId: 1,
            bayNumber: request.BayNumber,
            passcode: passcode,
            minutes: 15
        );

        return Ok(new
        {
            success = true,
            bookingId = created.ReservationId.ToString(),
            passcode = passcode,
            bayNumber = request.BayNumber,
            expiresAt = created.EndTime
        });
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive([FromQuery] string userId)
    {
        if (!long.TryParse(userId, out long uid)) uid = 1;

        var bookings = await _bookingRepository.GetActiveBookingsAsync(uid);
        var dtos = bookings.Select(b => new BookingResponseDto(
            b.ReservationId.ToString(),
            b.StationId.ToString(),
            b.StationName ?? "ConnectEV Station",
            1,
            "EV-389",
            b.ReservationStatus,
            b.EndTime
        ));

        return Ok(new { success = true, bookings = dtos });
    }
}
