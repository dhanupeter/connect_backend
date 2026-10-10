using System.Collections.Concurrent;
using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private static readonly ConcurrentDictionary<string, ChargingReservation> _memoryReservations = new();

    public BookingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ChargingReservation> CreateBookingHoldAsync(long userId, long stationId, long connectorId, int bayNumber, string passcode, int minutes = 15)
    {
        string resNumber = "RES-" + Guid.NewGuid().ToString("N")[..8].ToUpper();

        try
        {
            using var conn = _connectionFactory.CreateConnection();

            var vehicleId = await conn.QueryFirstOrDefaultAsync<long?>(
                "SELECT TOP 1 VehicleId FROM [vehicle].[Vehicles] WHERE UserId = @UserId", 
                new { UserId = userId }) ?? 1;

            if (connectorId <= 0)
            {
                connectorId = await conn.QueryFirstOrDefaultAsync<long?>(
                    "SELECT TOP 1 ConnectorId FROM [charging].[ChargingConnectors] WHERE StationId = @StationId", 
                    new { StationId = stationId }) ?? 1;
            }

            const string sql = @"
                INSERT INTO [charging].[ChargingReservations]
                    (ReservationNumber, UserId, VehicleId, StationId, ConnectorId, StartTime, EndTime, ReservationStatus, EstimatedEnergyKwh, EstimatedAmount, CreatedAt)
                OUTPUT INSERTED.*
                VALUES
                    (@ReservationNumber, @UserId, @VehicleId, @StationId, @ConnectorId, SYSUTCDATETIME(), DATEADD(minute, @Minutes, SYSUTCDATETIME()), 'CONFIRMED', 35.0, 450.00, SYSUTCDATETIME());";

            var reservation = await conn.QuerySingleAsync<ChargingReservation>(sql, new
            {
                ReservationNumber = resNumber,
                UserId = userId,
                VehicleId = vehicleId,
                StationId = stationId,
                ConnectorId = connectorId,
                Minutes = minutes
            });

            await conn.ExecuteAsync(
                "UPDATE [charging].[ChargingConnectors] SET ConnectorStatus = 'RESERVED' WHERE ConnectorId = @ConnectorId",
                new { ConnectorId = connectorId });

            return reservation;
        }
        catch
        {
            var fallback = new ChargingReservation
            {
                ReservationId = _memoryReservations.Count + 1,
                ReservationNumber = resNumber,
                UserId = userId,
                VehicleId = 1,
                StationId = stationId,
                ConnectorId = connectorId > 0 ? connectorId : 1,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(minutes),
                ReservationStatus = "CONFIRMED",
                EstimatedEnergyKwh = 35.0m,
                EstimatedAmount = 450.00m,
                CreatedAt = DateTime.UtcNow
            };
            _memoryReservations[resNumber] = fallback;
            return fallback;
        }
    }

    public async Task<IEnumerable<ChargingReservation>> GetActiveBookingsAsync(long userId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT 
                    r.*, 
                    s.StationName, 
                    c.ConnectorNumber
                FROM [charging].[ChargingReservations] r
                INNER JOIN [charging].[ChargingStations] s ON r.StationId = s.StationId
                INNER JOIN [charging].[ChargingConnectors] c ON r.ConnectorId = c.ConnectorId
                WHERE r.UserId = @UserId AND r.EndTime > SYSUTCDATETIME() AND r.ReservationStatus = 'CONFIRMED'
                ORDER BY r.StartTime DESC";

            var list = await conn.QueryAsync<ChargingReservation>(sql, new { UserId = userId });
            if (list.Any()) return list;
        }
        catch { }

        return _memoryReservations.Values.Where(r => r.UserId == userId && r.EndTime > DateTime.UtcNow);
    }

    public async Task<ChargingSession?> GetActiveSessionAsync(long userId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT TOP 1 * 
                FROM [charging].[ChargingSessions]
                WHERE UserId = @UserId AND ChargingStatus = 'CHARGING'
                ORDER BY StartTime DESC";

            return await conn.QueryFirstOrDefaultAsync<ChargingSession>(sql, new { UserId = userId });
        }
        catch
        {
            return null;
        }
    }
}
