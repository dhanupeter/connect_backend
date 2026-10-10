using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class StationRepository : IStationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    private static readonly List<ChargingStation> _fallbackStations = new()
    {
        new()
        {
            StationId = 1,
            StationCode = "STN-AVINASHI-01",
            StationName = "Zeon Fast Charging - Avinashi Highway",
            OperatorId = 1,
            Latitude = 11.2612m,
            Longitude = 77.2685m,
            Is24Hours = true,
            StationStatus = "OPERATIONAL",
            Connectors = new List<ChargingConnector>
            {
                new() { ConnectorId = 1, StationId = 1, ConnectorNumber = "BAY-01", ChargingStandardId = 1, StandardName = "CCS2", PowerType = "DC", MaxPowerKw = 60.0m, PricePerKwh = 18.50m, ConnectorStatus = "AVAILABLE" },
                new() { ConnectorId = 2, StationId = 1, ConnectorNumber = "BAY-02", ChargingStandardId = 1, StandardName = "CCS2", PowerType = "DC", MaxPowerKw = 60.0m, PricePerKwh = 18.50m, ConnectorStatus = "OCCUPIED" },
                new() { ConnectorId = 3, StationId = 1, ConnectorNumber = "BAY-03", ChargingStandardId = 2, StandardName = "Type 2", PowerType = "AC", MaxPowerKw = 22.0m, PricePerKwh = 14.00m, ConnectorStatus = "AVAILABLE" }
            }
        },
        new()
        {
            StationId = 2,
            StationCode = "STN-BROOK-02",
            StationName = "Tata Power EZ Charge - Brookefields Mall",
            OperatorId = 2,
            Latitude = 11.0089m,
            Longitude = 76.9602m,
            Is24Hours = true,
            StationStatus = "OPERATIONAL",
            Connectors = new List<ChargingConnector>
            {
                new() { ConnectorId = 4, StationId = 2, ConnectorNumber = "BAY-01", ChargingStandardId = 1, StandardName = "CCS2", PowerType = "DC", MaxPowerKw = 30.0m, PricePerKwh = 19.00m, ConnectorStatus = "AVAILABLE" },
                new() { ConnectorId = 5, StationId = 2, ConnectorNumber = "BAY-02", ChargingStandardId = 2, StandardName = "Type 2", PowerType = "AC", MaxPowerKw = 7.4m, PricePerKwh = 13.50m, ConnectorStatus = "AVAILABLE" }
            }
        }
    };

    public StationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ChargingStation>> GetAllStationsAsync()
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string stationSql = "SELECT * FROM [charging].[ChargingStations] WHERE StationStatus = 'OPERATIONAL'";
            var stations = (await conn.QueryAsync<ChargingStation>(stationSql)).ToList();

            if (stations.Any())
            {
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
        }
        catch { }

        return _fallbackStations;
    }

    public async Task<ChargingStation?> GetByIdAsync(long stationId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 * FROM [charging].[ChargingStations] WHERE StationId = @StationId";
            var station = await conn.QueryFirstOrDefaultAsync<ChargingStation>(sql, new { StationId = stationId });
            if (station != null)
            {
                station.Connectors = (await GetConnectorsByStationIdAsync(stationId)).ToList();
                return station;
            }
        }
        catch { }

        return _fallbackStations.FirstOrDefault(s => s.StationId == stationId);
    }

    public async Task<IEnumerable<ChargingConnector>> GetConnectorsByStationIdAsync(long stationId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT c.*, s.Name AS StandardName 
                FROM [charging].[ChargingConnectors] c
                INNER JOIN [charging].[ChargingStandards] s ON c.ChargingStandardId = s.ChargingStandardId
                WHERE c.StationId = @StationId";

            var connectors = (await conn.QueryAsync<ChargingConnector>(sql, new { StationId = stationId })).ToList();
            if (connectors.Any()) return connectors;
        }
        catch { }

        var fallback = _fallbackStations.FirstOrDefault(s => s.StationId == stationId);
        return fallback?.Connectors ?? Enumerable.Empty<ChargingConnector>();
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
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            var list = await conn.QueryAsync<Manufacturer>("SELECT * FROM [vehicle].[Manufacturers] ORDER BY ManufacturerName");
            if (list.Any()) return list;
        }
        catch { }

        return new List<Manufacturer>
        {
            new() { ManufacturerId = 1, ManufacturerCode = "TATA", ManufacturerName = "Tata Motors", CountryCode = "IN" },
            new() { ManufacturerId = 2, ManufacturerCode = "MG", ManufacturerName = "MG Motor", CountryCode = "GB" },
            new() { ManufacturerId = 3, ManufacturerCode = "HYUNDAI", ManufacturerName = "Hyundai", CountryCode = "KR" },
            new() { ManufacturerId = 4, ManufacturerCode = "BYD", ManufacturerName = "BYD", CountryCode = "CN" },
            new() { ManufacturerId = 5, ManufacturerCode = "MAHINDRA", ManufacturerName = "Mahindra", CountryCode = "IN" }
        };
    }

    public async Task<IEnumerable<VehicleModel>> GetModelsByManufacturerAsync(int manufacturerId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            var list = await conn.QueryAsync<VehicleModel>("SELECT * FROM [vehicle].[VehicleModels] WHERE ManufacturerId = @ManufacturerId", new { ManufacturerId = manufacturerId });
            if (list.Any()) return list;
        }
        catch { }

        return new List<VehicleModel>
        {
            new() { ModelId = 1, ManufacturerId = manufacturerId, ModelCode = "NEXON_EV", ModelName = "Nexon EV" },
            new() { ModelId = 2, ManufacturerId = manufacturerId, ModelCode = "TIAGO_EV", ModelName = "Tiago EV" }
        };
    }

    public async Task<IEnumerable<VehicleVariant>> GetVariantsByModelAsync(int modelId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            var list = await conn.QueryAsync<VehicleVariant>("SELECT * FROM [vehicle].[VehicleVariants] WHERE ModelId = @ModelId", new { ModelId = modelId });
            if (list.Any()) return list;
        }
        catch { }

        return new List<VehicleVariant>
        {
            new() { VariantId = 1, ModelId = modelId, VariantName = "Empowered+ 45 Long Range", BatteryCapacityKwh = 45.0m, RangeKm = 489, MaxChargingPowerKw = 60.0m }
        };
    }

    public async Task<IEnumerable<ChargingStandard>> GetChargingStandardsAsync()
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            var list = await conn.QueryAsync<ChargingStandard>("SELECT * FROM [charging].[ChargingStandards]");
            if (list.Any()) return list;
        }
        catch { }

        return new List<ChargingStandard>
        {
            new() { ChargingStandardId = 1, Code = "CCS2", Name = "CCS2", Description = "Combined Charging System Combo 2" },
            new() { ChargingStandardId = 2, Code = "TYPE2", Name = "Type 2", Description = "Mennekes AC Connector" },
            new() { ChargingStandardId = 3, Code = "GBT", Name = "GB/T", Description = "Chinese National Standard" }
        };
    }

    public async Task<IEnumerable<Role>> GetRolesAsync()
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            var list = await conn.QueryAsync<Role>("SELECT * FROM [identity].[Roles]");
            if (list.Any()) return list;
        }
        catch { }

        return new List<Role>
        {
            new() { RoleId = 1, RoleCode = "EV_OWNER", RoleName = "EV Driver & Owner" },
            new() { RoleId = 2, RoleCode = "CPO_OPERATOR", RoleName = "Charge Point Operator" },
            new() { RoleId = 3, RoleCode = "SUPER_ADMIN", RoleName = "System Administrator" }
        };
    }
}
