using ConnectBackend.Models;

namespace ConnectBackend.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id);
    Task<User?> GetByFirebaseUidAsync(string firebaseUid);
    Task<User?> GetByEmailAsync(string email);
    Task<User> UpsertUserAsync(User user, string? defaultRole = "EV_OWNER");
    Task UpdateLastLoginAsync(long userId);
}

public interface IVehicleRepository
{
    Task<Vehicle?> GetByUserIdAsync(long userId);
    Task<Vehicle?> GetByIdAsync(long vehicleId);
    Task<Vehicle> UpsertVehicleAsync(Vehicle vehicle, string? modelCode = null, string? makeCode = null);
    Task<VehicleTelemetry?> GetLatestTelemetryAsync(long vehicleId);
    Task RecordTelemetryAsync(VehicleTelemetry telemetry);
}

public interface IBookingRepository
{
    Task<ChargingReservation> CreateBookingHoldAsync(long userId, long stationId, long connectorId, int bayNumber, string passcode, int minutes = 15);
    Task<IEnumerable<ChargingReservation>> GetActiveBookingsAsync(long userId);
    Task<ChargingSession?> GetActiveSessionAsync(long userId);
}

public interface IStationRepository
{
    Task<IEnumerable<ChargingStation>> GetAllStationsAsync();
    Task<ChargingStation?> GetByIdAsync(long stationId);
    Task<IEnumerable<ChargingConnector>> GetConnectorsByStationIdAsync(long stationId);
}

public interface IMasterDataRepository
{
    Task<IEnumerable<Manufacturer>> GetManufacturersAsync();
    Task<IEnumerable<VehicleModel>> GetModelsByManufacturerAsync(int manufacturerId);
    Task<IEnumerable<VehicleVariant>> GetVariantsByModelAsync(int modelId);
    Task<IEnumerable<ChargingStandard>> GetChargingStandardsAsync();
    Task<IEnumerable<Role>> GetRolesAsync();
}
