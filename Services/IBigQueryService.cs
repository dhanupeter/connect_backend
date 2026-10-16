using ConnectBackend.Models;
using Google.Cloud.BigQuery.V2;

namespace ConnectBackend.Services;

public interface IBigQueryService
{
    bool IsConfigured { get; }
    string ProjectId { get; }
    string DatasetId { get; }
    Task<BigQueryResults?> ExecuteQueryAsync(string sql);
    Task<IEnumerable<string>> GetTablesAsync();
    Task<long> GetTableRowCountAsync(string tableName);
    Task InsertChatMessageAsync(ChatMessage message);
    Task InsertConversationAsync(Conversation conv);
    Task<IEnumerable<Conversation>> GetConversationsAsync(long userId);
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId, int limit);
    Task<IEnumerable<NearbyUserDto>> GetNearbyUsersAsync(double lat, double lng, double radiusKm);
    Task<IEnumerable<NearbyUserDto>> GetContactsAsync();
    Task UpdateUserLocationAsync(long userId, double lat, double lng, bool isDiscoverable);
    Task InsertTelemetryAsync(VehicleTelemetry telemetry);
    Task InsertReservationAsync(ChargingReservation res);
    Task InsertOrUpdateUserAsync(User user);
    Task InsertOrUpdateVehicleAsync(Vehicle vehicle);
    Task SetDiscoverableAsync(long userId, bool isDiscoverable);
    Task RegisterDeviceAsync(DeviceInfoDto device);
    Task<IEnumerable<string>> GetDeviceTokensAsync(long userId);
    Task InsertActivityLogAsync(ActivityLog log);
    Task<IEnumerable<ActivityLog>> GetActivityLogsAsync(string? status, string? category, int limit);
    Task SetSecretAsync(string secretKey, string encryptedData, string algorithm = "AES-256-CBC");
    Task<string?> GetSecretAsync(string secretKey);
    Task EnsurePreferencesTableAsync();
    Task<Conversation?> UpdateGroupAsync(string conversationId, UpdateGroupDto dto);
    Task<bool> AddMemberAsync(string conversationId, long userId, bool isAdmin);
    Task<bool> RemoveMemberAsync(string conversationId, long userId);
    Task<bool> UpdateMemberRoleAsync(string conversationId, long userId, bool isAdmin);
    Task<IEnumerable<long>> GetGroupParticipantUserIdsAsync(string conversationId);
    Task<bool> IsMutedOrBlockedAsync(long userId, string conversationId);
}

