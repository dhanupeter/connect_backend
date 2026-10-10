using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class VehicleRepository : IVehicleRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VehicleRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Vehicle?> GetByUserIdAsync(long userId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT TOP 1 
                v.*, 
                m.ManufacturerName, 
                md.ModelName,
                var.VariantName,
                var.BatteryCapacityKwh,
                var.RangeKm
            FROM [vehicle].[Vehicles] v
            LEFT JOIN [vehicle].[Manufacturers] m ON v.ManufacturerId = m.ManufacturerId
            LEFT JOIN [vehicle].[VehicleModels] md ON v.ModelId = md.ModelId
            LEFT JOIN [vehicle].[VehicleVariants] var ON v.VariantId = var.VariantId
            WHERE v.UserId = @UserId
            ORDER BY v.VehicleId DESC";

        return await conn.QueryFirstOrDefaultAsync<Vehicle>(sql, new { UserId = userId });
    }

    public async Task<Vehicle?> GetByIdAsync(long vehicleId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT TOP 1 
                v.*, 
                m.ManufacturerName, 
                md.ModelName,
                var.VariantName,
                var.BatteryCapacityKwh,
                var.RangeKm
            FROM [vehicle].[Vehicles] v
            LEFT JOIN [vehicle].[Manufacturers] m ON v.ManufacturerId = m.ManufacturerId
            LEFT JOIN [vehicle].[VehicleModels] md ON v.ModelId = md.ModelId
            LEFT JOIN [vehicle].[VehicleVariants] var ON v.VariantId = var.VariantId
            WHERE v.VehicleId = @VehicleId";

        return await conn.QueryFirstOrDefaultAsync<Vehicle>(sql, new { VehicleId = vehicleId });
    }

    public async Task<Vehicle> UpsertVehicleAsync(Vehicle vehicle, string? modelCode = null, string? makeCode = null)
    {
        using var conn = _connectionFactory.CreateConnection();

        // 1. Resolve Manufacturer and Model IDs if not set
        if (vehicle.ManufacturerId <= 0)
        {
            var mId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT TOP 1 ManufacturerId FROM [vehicle].[Manufacturers] WHERE ManufacturerCode = @Code",
                new { Code = makeCode ?? "TATA" });

            vehicle.ManufacturerId = mId ?? 1;
        }

        if (vehicle.ModelId <= 0)
        {
            var moId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT TOP 1 ModelId FROM [vehicle].[VehicleModels] WHERE ManufacturerId = @MId",
                new { MId = vehicle.ManufacturerId });

            vehicle.ModelId = moId ?? 1;
        }

        if (string.IsNullOrWhiteSpace(vehicle.VehicleCode))
        {
            vehicle.VehicleCode = "VEH-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        }

        // 2. Check if user already has a vehicle
        var existing = await conn.QueryFirstOrDefaultAsync<Vehicle>(
            "SELECT TOP 1 * FROM [vehicle].[Vehicles] WHERE UserId = @UserId",
            new { vehicle.UserId });

        if (existing != null)
        {
            const string updateSql = @"
                UPDATE [vehicle].[Vehicles]
                SET RegistrationNumber = @RegistrationNumber,
                    Color = COALESCE(@Color, Color),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE VehicleId = @VehicleId";

            await conn.ExecuteAsync(updateSql, new
            {
                existing.VehicleId,
                vehicle.RegistrationNumber,
                vehicle.Color
            });

            return (await GetByIdAsync(existing.VehicleId))!;
        }

        // 3. Insert new vehicle
        const string insertSql = @"
            INSERT INTO [vehicle].[Vehicles]
                (UserId, VehicleCode, RegistrationNumber, VIN, ManufacturerId, ModelId, VariantId, Color, IsElectric, Status, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.VehicleId
            VALUES
                (@UserId, @VehicleCode, @RegistrationNumber, @VIN, @ManufacturerId, @ModelId, @VariantId, @Color, 1, 'ACTIVE', SYSUTCDATETIME(), SYSUTCDATETIME());";

        var newVehicleId = await conn.ExecuteScalarAsync<long>(insertSql, vehicle);

        // 4. Auto-assign vehicle owner
        const string ownerSql = @"
            INSERT INTO [vehicle].[VehicleOwners] (VehicleId, UserId, OwnershipType, OwnershipPercentage, StartDate, IsCurrentOwner)
            VALUES (@VehicleId, @UserId, 'INDIVIDUAL', 100.00, CAST(GETDATE() AS DATE), 1);";

        await conn.ExecuteAsync(ownerSql, new { VehicleId = newVehicleId, vehicle.UserId });

        // 5. Seed initial Telemetry
        const string telemetrySql = @"
            INSERT INTO [telemetry].[VehicleTelemetry]
                (VehicleId, RecordedAt, SOC, SpeedKmh, RangeKm, BatteryTemperature, ChargingStatus, IgnitionStatus)
            VALUES
                (@VehicleId, SYSUTCDATETIME(), 82.0, 0.0, 342.0, 28.5, 'DISCONNECTED', 'OFF');";

        await conn.ExecuteAsync(telemetrySql, new { VehicleId = newVehicleId });

        return (await GetByIdAsync(newVehicleId))!;
    }

    public async Task<VehicleTelemetry?> GetLatestTelemetryAsync(long vehicleId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT TOP 1 * 
            FROM [telemetry].[VehicleTelemetry] 
            WHERE VehicleId = @VehicleId 
            ORDER BY RecordedAt DESC";

        return await conn.QueryFirstOrDefaultAsync<VehicleTelemetry>(sql, new { VehicleId = vehicleId });
    }

    public async Task RecordTelemetryAsync(VehicleTelemetry telemetry)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            INSERT INTO [telemetry].[VehicleTelemetry]
                (VehicleId, RecordedAt, Latitude, Longitude, SpeedKmh, SOC, BatteryVoltage, BatteryCurrent, BatteryTemperature, MotorTemperature, ChargingPowerKw, RangeKm, OdometerKm, ChargingStatus, IgnitionStatus)
            VALUES
                (@VehicleId, SYSUTCDATETIME(), @Latitude, @Longitude, @SpeedKmh, @SOC, @BatteryVoltage, @BatteryCurrent, @BatteryTemperature, @MotorTemperature, @ChargingPowerKw, @RangeKm, @OdometerKm, @ChargingStatus, @IgnitionStatus);";

        await conn.ExecuteAsync(sql, telemetry);
    }
}
