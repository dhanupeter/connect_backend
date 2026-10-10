using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class StationRepository : IStationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ChargingStation>> GetAllStationsAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        const string stationSql = "SELECT * FROM [charging].[ChargingStations] WHERE StationStatus = 'OPERATIONAL'";
        var stations = (await conn.QueryAsync<ChargingStation>(stationSql)).ToList();

        const string connectorSql = @"
            SELECT c.*, s.Name AS StandardName 
            FROM [charging].[ChargingConnectors] c
            INNER JOIN [charging].[ChargingStandards] s ON c.ChargingStandardId = s.ChargingStandardId";
        var allConnectors = (await conn.QueryAsync<ChargingConnector>(connectorSql)).ToList();

        foreach (var stn in stations)
        {
            stn.Connectors = allConnectors.Where(c => c.StationId == stn.StationId).ToList();
        }

        return stations;
    }

    public async Task<ChargingStation?> GetByIdAsync(long stationId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = "SELECT TOP 1 * FROM [charging].[ChargingStations] WHERE StationId = @StationId";
        var station = await conn.QueryFirstOrDefaultAsync<ChargingStation>(sql, new { StationId = stationId });
        if (station != null)
        {
            station.Connectors = (await GetConnectorsByStationIdAsync(stationId)).ToList();
        }
        return station;
    }

    public async Task<IEnumerable<ChargingConnector>> GetConnectorsByStationIdAsync(long stationId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT c.*, s.Name AS StandardName 
            FROM [charging].[ChargingConnectors] c
            INNER JOIN [charging].[ChargingStandards] s ON c.ChargingStandardId = s.ChargingStandardId
            WHERE c.StationId = @StationId";

        return await conn.QueryAsync<ChargingConnector>(sql, new { StationId = stationId });
    }
}

public class MasterDataRepository : IMasterDataRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MasterDataRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Manufacturer>> GetManufacturersAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Manufacturer>("SELECT * FROM [vehicle].[Manufacturers] ORDER BY ManufacturerName");
    }

    public async Task<IEnumerable<VehicleModel>> GetModelsByManufacturerAsync(int manufacturerId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<VehicleModel>("SELECT * FROM [vehicle].[VehicleModels] WHERE ManufacturerId = @ManufacturerId", new { ManufacturerId = manufacturerId });
    }

    public async Task<IEnumerable<VehicleVariant>> GetVariantsByModelAsync(int modelId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<VehicleVariant>("SELECT * FROM [vehicle].[VehicleVariants] WHERE ModelId = @ModelId", new { ModelId = modelId });
    }

    public async Task<IEnumerable<ChargingStandard>> GetChargingStandardsAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ChargingStandard>("SELECT * FROM [charging].[ChargingStandards]");
    }

    public async Task<IEnumerable<Role>> GetRolesAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Role>("SELECT * FROM [identity].[Roles]");
    }
}
