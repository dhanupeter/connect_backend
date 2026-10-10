namespace ConnectBackend.Models;

// ==========================================
// 1. IDENTITY & USER MANAGEMENT
// ==========================================
public class User
{
    public long UserId { get; set; }
    public string FirebaseUid { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? MobileNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public bool IsEmailVerified { get; set; }
    public bool IsMobileVerified { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Role
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class UserRole
{
    public long UserRoleId { get; set; }
    public long UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }
    public long? AssignedBy { get; set; }
}

public class UserDevice
{
    public long DeviceId { get; set; }
    public long UserId { get; set; }
    public string DeviceToken { get; set; } = string.Empty;
    public string Platform { get; set; } = "ANDROID";
    public string? DeviceModel { get; set; }
    public string? AppVersion { get; set; }
    public string? OSVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ==========================================
// 2. VEHICLE MASTER & MODELS
// ==========================================
public class Manufacturer
{
    public int ManufacturerId { get; set; }
    public string ManufacturerCode { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class VehicleModel
{
    public int ModelId { get; set; }
    public int ManufacturerId { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string VehicleCategory { get; set; } = "4-WHEELER";
    public short? LaunchYear { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class VehicleVariant
{
    public int VariantId { get; set; }
    public int ModelId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public decimal? BatteryCapacityKwh { get; set; }
    public decimal? RangeKm { get; set; }
    public decimal? MaxChargingPowerKw { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class Vehicle
{
    public long VehicleId { get; set; }
    public long UserId { get; set; }
    public string VehicleCode { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? VIN { get; set; }
    public int ManufacturerId { get; set; }
    public int ModelId { get; set; }
    public int? VariantId { get; set; }
    public int? VehicleTypeId { get; set; }
    public int? FuelTypeId { get; set; }
    public short? ManufactureYear { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public string? Color { get; set; }
    public bool IsElectric { get; set; } = true;
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Joined fields for easy display
    public string? ManufacturerName { get; set; }
    public string? ModelName { get; set; }
    public string? VariantName { get; set; }
    public decimal? BatteryCapacityKwh { get; set; }
    public decimal? RangeKm { get; set; }
}

public class VehicleOwner
{
    public long VehicleOwnerId { get; set; }
    public long VehicleId { get; set; }
    public long UserId { get; set; }
    public string OwnershipType { get; set; } = "INDIVIDUAL";
    public decimal OwnershipPercentage { get; set; } = 100.00m;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrentOwner { get; set; } = true;
}

public class VehicleDriver
{
    public long VehicleDriverId { get; set; }
    public long VehicleId { get; set; }
    public long UserId { get; set; }
    public string DriverType { get; set; } = "PRIMARY";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

// ==========================================
// 3. EV BATTERY & HEALTH
// ==========================================
public class EVBattery
{
    public long BatteryId { get; set; }
    public long VehicleId { get; set; }
    public string? BatterySerialNumber { get; set; }
    public string? BatteryType { get; set; }
    public string? Chemistry { get; set; }
    public decimal NominalCapacityKwh { get; set; }
    public decimal UsableCapacityKwh { get; set; }
    public decimal? Voltage { get; set; }
    public decimal? CurrentCapacityAh { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? InstallationDate { get; set; }
    public DateTime? WarrantyExpiryDate { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class BatteryHealth
{
    public long BatteryHealthId { get; set; }
    public long BatteryId { get; set; }
    public decimal HealthPercentage { get; set; }
    public decimal StateOfCharge { get; set; }
    public int? CycleCount { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Voltage { get; set; }
    public DateTime RecordedAt { get; set; }
}

// ==========================================
// 4. EV CHARGING & STATIONS
// ==========================================
public class ChargingStandard
{
    public int ChargingStandardId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class VehicleChargingCapability
{
    public long VehicleChargingCapabilityId { get; set; }
    public long VehicleId { get; set; }
    public int ChargingStandardId { get; set; }
    public decimal MaxChargingPowerKw { get; set; }
    public decimal? MaxVoltage { get; set; }
    public decimal? MaxCurrent { get; set; }
    public bool SupportsFastCharging { get; set; } = true;
    public bool SupportsUltraFast { get; set; } = false;
}

public class ChargingStationOperator
{
    public long OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? CompanyRegistrationNo { get; set; }
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public class ChargingStation
{
    public long StationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public long OperatorId { get; set; }
    public long? AddressId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public TimeSpan? OpeningTime { get; set; }
    public TimeSpan? ClosingTime { get; set; }
    public bool Is24Hours { get; set; } = true;
    public string StationStatus { get; set; } = "OPERATIONAL";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Connectors list (navigation/joined)
    public List<ChargingConnector> Connectors { get; set; } = new();
}

public class ChargingConnector
{
    public long ConnectorId { get; set; }
    public long StationId { get; set; }
    public string ConnectorNumber { get; set; } = string.Empty;
    public int ChargingStandardId { get; set; }
    public string PowerType { get; set; } = "DC";
    public decimal MaxPowerKw { get; set; }
    public decimal? Voltage { get; set; }
    public decimal? Current { get; set; }
    public string ConnectorStatus { get; set; } = "AVAILABLE";
    public decimal PricePerKwh { get; set; }
    public decimal PricePerMinute { get; set; }
    public string? StandardName { get; set; }
}

public class ConnectorAvailability
{
    public long AvailabilityId { get; set; }
    public long ConnectorId { get; set; }
    public string Status { get; set; } = "AVAILABLE";
    public long? CurrentVehicleId { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}

// ==========================================
// 5. RESERVATIONS & SESSIONS
// ==========================================
public class ChargingReservation
{
    public long ReservationId { get; set; }
    public string ReservationNumber { get; set; } = string.Empty;
    public long UserId { get; set; }
    public long VehicleId { get; set; }
    public long StationId { get; set; }
    public long ConnectorId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string ReservationStatus { get; set; } = "CONFIRMED";
    public decimal? EstimatedEnergyKwh { get; set; }
    public decimal EstimatedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Display fields
    public string? StationName { get; set; }
    public string? ConnectorNumber { get; set; }
}

public class ChargingSession
{
    public long ChargingSessionId { get; set; }
    public string SessionNumber { get; set; } = string.Empty;
    public long? ReservationId { get; set; }
    public long UserId { get; set; }
    public long VehicleId { get; set; }
    public long StationId { get; set; }
    public long ConnectorId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public decimal? StartSOC { get; set; }
    public decimal? EndSOC { get; set; }
    public decimal EnergyConsumedKwh { get; set; }
    public decimal? PeakPowerKw { get; set; }
    public decimal? AveragePowerKw { get; set; }
    public long DurationSeconds { get; set; }
    public string ChargingStatus { get; set; } = "CHARGING";
    public decimal TotalAmount { get; set; }
}

// ==========================================
// 6. TELEMETRY & TRIPS
// ==========================================
public class VehicleTelemetry
{
    public long TelemetryId { get; set; }
    public long VehicleId { get; set; }
    public DateTime RecordedAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? SpeedKmh { get; set; }
    public decimal SOC { get; set; }
    public decimal? BatteryVoltage { get; set; }
    public decimal? BatteryCurrent { get; set; }
    public decimal? BatteryTemperature { get; set; }
    public decimal? MotorTemperature { get; set; }
    public decimal? ChargingPowerKw { get; set; }
    public decimal? RangeKm { get; set; }
    public decimal? OdometerKm { get; set; }
    public string? ChargingStatus { get; set; }
    public string? IgnitionStatus { get; set; }
}

public class Trip
{
    public long TripId { get; set; }
    public long VehicleId { get; set; }
    public long UserId { get; set; }
    public decimal? StartLatitude { get; set; }
    public decimal? StartLongitude { get; set; }
    public decimal? EndLatitude { get; set; }
    public decimal? EndLongitude { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal DistanceKm { get; set; }
    public decimal? EnergyConsumedKwh { get; set; }
    public decimal? StartSOC { get; set; }
    public decimal? EndSOC { get; set; }
    public decimal? AverageSpeedKmh { get; set; }
}

// ==========================================
// 7. VAHAN INTEGRATION
// ==========================================
public class VahanVehicleDetail
{
    public long VahanVehicleDetailId { get; set; }
    public long? VehicleId { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? OwnerNameMasked { get; set; }
    public string? VehicleClass { get; set; }
    public string? FuelType { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public DateTime? RegistrationDate { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? FitnessUpto { get; set; }
    public DateTime? InsuranceUpto { get; set; }
    public DateTime? PollutionUpto { get; set; }
    public DateTime? PermitUpto { get; set; }
    public DateTime? TaxUpto { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime LastVerifiedAt { get; set; }
}
