using ConnectBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BigQueryController : ControllerBase
{
    private readonly IBigQueryService _bqService;

    public BigQueryController(IBigQueryService bqService)
    {
        _bqService = bqService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var isConfigured = _bqService.IsConfigured;
        var tables = isConfigured ? await _bqService.GetTablesAsync() : Enumerable.Empty<string>();

        return Ok(new
        {
            success = true,
            isConfigured,
            projectId = _bqService.ProjectId,
            datasetId = _bqService.DatasetId,
            tableCount = tables.Count(),
            tables
        });
    }

    [HttpGet("tables")]
    public async Task<IActionResult> GetTables()
    {
        var tables = await _bqService.GetTablesAsync();
        return Ok(new { success = true, count = tables.Count(), tables });
    }

    [HttpPost("query")]
    public async Task<IActionResult> RunQuery([FromBody] QueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BadRequest(new { success = false, message = "SQL query string is required." });
        }

        var results = await _bqService.ExecuteQueryAsync(request.Sql);
        if (results == null)
        {
            return StatusCode(500, new { success = false, message = "Query execution failed or BigQuery is unconfigured." });
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in results)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var field in results.Schema.Fields)
            {
                dict[field.Name] = row[field.Name];
            }
            rows.Add(dict);
        }

        return Ok(new
        {
            success = true,
            totalRows = results.TotalRows,
            rowCount = rows.Count,
            data = rows
        });
    }
}

public class QueryRequest
{
    public string Sql { get; set; } = string.Empty;
}
