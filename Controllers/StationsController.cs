using ConnectBackend.Dtos;
using ConnectBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ConnectBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private readonly IStationRepository _stationRepository;

    public StationsController(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var stations = await _stationRepository.GetAllStationsAsync();
        var dtos = stations.Select(s => new StationDto(
            s.StationId,
            s.StationCode,
            s.StationName,
            s.Latitude,
            s.Longitude,
            s.Is24Hours,
            s.StationStatus,
            s.Connectors.Select(c => new ConnectorDto(
                c.ConnectorId,
                c.ConnectorNumber,
                c.PowerType,
                c.MaxPowerKw,
                c.PricePerKwh,
                c.ConnectorStatus,
                c.StandardName
            )).ToList()
        ));

        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var station = await _stationRepository.GetByIdAsync(id);
        if (station == null)
        {
            return NotFound(new { message = "Station not found" });
        }

        return Ok(new StationDto(
            station.StationId,
            station.StationCode,
            station.StationName,
            station.Latitude,
            station.Longitude,
            station.Is24Hours,
            station.StationStatus,
            station.Connectors.Select(c => new ConnectorDto(
                c.ConnectorId,
                c.ConnectorNumber,
                c.PowerType,
                c.MaxPowerKw,
                c.PricePerKwh,
                c.ConnectorStatus,
                c.StandardName
            )).ToList()
        ));
    }
}

[ApiController]
[Route("api/[controller]")]
public class MasterDataController : ControllerBase
{
    private readonly IMasterDataRepository _masterDataRepository;

    public MasterDataController(IMasterDataRepository masterDataRepository)
    {
        _masterDataRepository = masterDataRepository;
    }

    [HttpGet("manufacturers")]
    public async Task<IActionResult> GetManufacturers()
    {
        return Ok(await _masterDataRepository.GetManufacturersAsync());
    }

    [HttpGet("manufacturers/{id}/models")]
    public async Task<IActionResult> GetModels(int id)
    {
        return Ok(await _masterDataRepository.GetModelsByManufacturerAsync(id));
    }

    [HttpGet("charging-standards")]
    public async Task<IActionResult> GetChargingStandards()
    {
        return Ok(await _masterDataRepository.GetChargingStandardsAsync());
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        return Ok(await _masterDataRepository.GetRolesAsync());
    }
}
