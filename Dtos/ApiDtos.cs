namespace ConnectBackend.Dtos;

// Auth DTOs
public record GoogleLoginRequest(
    string? FirebaseUid,
    string? Email,
    string? DisplayName,
    string? PhotoUrl
);

public record AuthResponseDto(
    bool Success,
    string Token,
    UserDto User,
    VehicleDto? Vehicle
);

public record UserDto(
    string Id,
    string FirebaseUid,
    string Email,
    string? DisplayName,
    string? Phone,
    string Role,
    string? PhotoUrl,
    long UserId = 0
);

public record VehicleDto(
    string Plate,
    string MakeModel,
    double BatteryCapacityKwh,
    string PortType,
    int EstimatedRangeKm,
    bool IsVahanVerified
);

public record UpdateVehicleRequest(
    string UserId,
    string Plate,
    string MakeModel,
    double BatteryCapacityKwh,
    string PortType,
    int EstimatedRangeKm
);

// Booking DTOs
public record CreateBookingRequest(
    string UserId,
    string StationId,
    string StationName,
    int BayNumber
);

public record BookingResponseDto(
    string Id,
    string StationId,
    string StationName,
    int BayNumber,
    string Passcode,
    string Status,
    DateTime ExpiresAt
);

// Station DTOs
public record StationDto(
    long StationId,
    string StationCode,
    string StationName,
    decimal Latitude,
    decimal Longitude,
    bool Is24Hours,
    string StationStatus,
    List<ConnectorDto> Connectors
);

public record ConnectorDto(
    long ConnectorId,
    string ConnectorNumber,
    string PowerType,
    decimal MaxPowerKw,
    decimal PricePerKwh,
    string Status,
    string? StandardName
);

// Telemetry DTO
public record TelemetryDto(
    long VehicleId,
    decimal SOC,
    decimal? SpeedKmh,
    decimal? RangeKm,
    decimal? BatteryTemperature,
    decimal? ChargingPowerKw,
    DateTime RecordedAt
);
