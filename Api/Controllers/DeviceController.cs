using Core.DTOs.Device;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	[ApiController]
	[Route("api/device")]
	public class DeviceController : ControllerBase
	{
		private readonly IDeviceService _deviceService;

		public DeviceController(IDeviceService deviceService)
		{
			_deviceService = deviceService;
		}

		/// <summary>
		/// Get all devices with optional filtering and pagination
		/// GET /api/devices
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> GetAll([FromQuery] DeviceFilterDto filter)
		{
			try
			{
				var result = await _deviceService.GetAllAsync(filter);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { status = 500, message = "An error occurred while retrieving devices", error = ex.Message });
			}
		}

		/// <summary>
		/// Get device by ID
		/// GET /api/devices/{id}
		/// </summary>
		[HttpGet("{id}")]
		public async Task<IActionResult> GetById(Guid id)
		{
			try
			{
				var result = await _deviceService.GetByIdAsync(id);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { status = 500, message = "An error occurred while retrieving device", error = ex.Message });
			}
		}

		/// <summary>
		/// Create new device
		/// POST /api/devices
		/// </summary>
		[HttpPost]
		public async Task<IActionResult> Create([FromBody] CreateDeviceDto dto)
		{
			try
			{
				if (!ModelState.IsValid)
				{
					return BadRequest(ModelState);
				}

				var result = await _deviceService.CreateAsync(dto);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { status = 500, message = "An error occurred while creating device", error = ex.Message });
			}
		}

		/// <summary>
		/// Update existing device
		/// PUT /api/devices/{id}
		/// </summary>
		[HttpPut("{id}")]
		public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceDto dto)
		{
			try
			{
				if (!ModelState.IsValid)
				{
					return BadRequest(ModelState);
				}

				var result = await _deviceService.UpdateAsync(id, dto);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { status = 500, message = "An error occurred while updating device", error = ex.Message });
			}
		}

		/// <summary>
		/// Delete device (soft delete)
		/// DELETE /api/devices/{id}
		/// </summary>
		[HttpDelete("{id}")]
		public async Task<IActionResult> Delete(Guid id)
		{
			try
			{
				var result = await _deviceService.DeleteAsync(id);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { status = 500, message = "An error occurred while deleting device", error = ex.Message });
			}
		}

		/// <summary>
		/// Test endpoint for HTTP device - returns random data
		/// GET /api/device/http-test
		/// </summary>
		[HttpGet("http-test")]
		[AllowAnonymous]
		public IActionResult HttpTest()
		{
			var random = new Random();
			var data = new
			{
				temperature = Math.Round(20 + random.NextDouble() * 15, 2),
				humidity = Math.Round(40 + random.NextDouble() * 40, 2),
				pressure = Math.Round(1000 + random.NextDouble() * 50, 2),
				vibration = Math.Round(random.NextDouble() * 10, 2),
				timestamp = DateTime.UtcNow
			};

			return Ok(data);
		}

	/// <summary>
	/// Test HTTP connection to external API (with actual request)
	/// POST /api/device/test-http-connection
	/// Support testing external APIs like OpenWeather, JSONPlaceholder, etc.
	/// </summary>
	[HttpPost("test-http-connection")]
	[AllowAnonymous]
	public async Task<IActionResult> TestHttpConnection([FromBody] TestHttpRequestDto request)
	{
		try
		{
			var result = await _deviceService.TestHttpConnectionAsync(request);
			return StatusCode(result.Status, result);
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { 
				status = 500, 
				message = "An error occurred while testing HTTP connection", 
				error = ex.Message 
			});
		}
	}
}
}
