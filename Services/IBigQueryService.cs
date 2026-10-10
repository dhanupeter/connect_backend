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
    Task InsertTelemetryAsync(VehicleTelemetry telemetry);
    Task InsertReservationAsync(ChargingReservation res);
}
