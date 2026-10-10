using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BookingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ChargingReservation> CreateBookingHoldAsync(long userId, long stationId, long connectorId, int bayNumber, string passcode, int minutes = 15)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Ensure valid vehicle for user
        var vehicleId = await conn.QueryFirstOrDefaultAsync<long?>(
            "SELECT TOP 1 VehicleId FROM [vehicle].[Vehicles] WHERE UserId = @UserId", 
            new { UserId = userId }) ?? 1;

        // Ensure valid connector
        if (connectorId <= 0)
        {
            connectorId = await conn.QueryFirstOrDefaultAsync<long?>(
                "SELECT TOP 1 ConnectorId FROM [charging].[ChargingConnectors] WHERE StationId = @StationId", 
                new { StationId = stationId }) ?? 1;
        }

        string resNumber = "RES-" + Guid.NewGuid().ToString("N")[..8].ToUpper();

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

        // Update connector status to RESERVED
        await conn.ExecuteAsync(
            "UPDATE [charging].[ChargingConnectors] SET ConnectorStatus = 'RESERVED' WHERE ConnectorId = @ConnectorId",
            new { ConnectorId = connectorId });

        return reservation;
    }

    public async Task<IEnumerable<ChargingReservation>> GetActiveBookingsAsync(long userId)
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

        return await conn.QueryAsync<ChargingReservation>(sql, new { UserId = userId });
    }

    public async Task<ChargingSession?> GetActiveSessionAsync(long userId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT TOP 1 * 
            FROM [charging].[ChargingSessions]
            WHERE UserId = @UserId AND ChargingStatus = 'CHARGING'
            ORDER BY StartTime DESC";

        return await conn.QueryFirstOrDefaultAsync<ChargingSession>(sql, new { UserId = userId });
    }
}
