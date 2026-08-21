using Core.DTOs.Tag;
using Core.DTOs;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	[ApiController]
	[Route("api/tags")]
	[Authorize]
	public class TagController : ControllerBase
	{
		private readonly ITagService _tagService;
		private readonly ITagHistoryService _historyService;
		private readonly ILogger<TagController> _logger;

		public TagController(
			ITagService tagService,
			ITagHistoryService historyService,
			ILogger<TagController> logger)
		{
			_tagService = tagService;
			_historyService = historyService;
			_logger = logger;
		}

		/// <summary>
		/// Nilai sekarang seluruh tag, dari RTDB di memori.
		/// GET /api/tags/current?deviceId=xxx
		///
		/// Dipakai dasbor saat pertama dibuka: tanpa ini, layar kosong sampai frame realtime
		/// berikutnya datang — yang untuk tag berinterval satu menit berarti kosong satu menit.
		/// </summary>
		[HttpGet("current")]
		public IActionResult GetCurrentValues([FromQuery] Guid? deviceId)
			=> Ok(_historyService.GetCurrentValues(deviceId));

		/// <summary>
		/// Riwayat satu tag dari historian.
		/// GET /api/tags/{id}/history?from=&to=&limit=2000&goodOnly=false
		/// </summary>
		[HttpGet("{id}/history")]
		public async Task<IActionResult> GetHistory(Guid id, [FromQuery] TagHistoryQueryDto query)
		{
			var response = await _historyService.GetAsync(id, query);
			return StatusCode(response.Status, response);
		}

		/// <summary>
		/// Get all tags with optional filtering
		/// GET /api/tags?deviceId=xxx&isActive=true&page=1&pageSize=50
		/// </summary>
		[HttpGet]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> GetAll([FromQuery] TagFilterDto filter)
		{
			try
			{
				_logger.LogInformation("Fetching tags with filter: {Filter}", filter);
				var result = await _tagService.GetAllAsync(filter);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tags");
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error retrieving tags"));
			}
		}

		/// <summary>
		/// Get tag by ID with current values
		/// GET /api/tags/{id}
		/// </summary>
		/// <summary>
		/// Membuat banyak tag sekaligus — dipakai pemilih key setelah pengguna memilih key mana
		/// yang mau dijadikan tag.
		/// POST /api/tags/bulk
		/// </summary>
		[HttpPost("bulk")]
		[Authorize(Roles = "ADMIN,ENGINEER")]
		public async Task<IActionResult> CreateBulk([FromBody] CreateTagsBulkDto dto)
		{
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values
					.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
					.Where(m => !string.IsNullOrWhiteSpace(m))
					.ToList();
				return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid", errors));
			}

			var result = await _tagService.CreateBulkAsync(dto);
			return StatusCode(result.Status, result);
		}

		[HttpGet("{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> GetById(Guid id)
		{
			try
			{
				_logger.LogInformation("Fetching tag: {TagId}", id);
				var result = await _tagService.GetByIdAsync(id);
				if (result == null)
					return NotFound(ApiResponse<object>.Fail(404, "Tag tidak ditemukan"));

				return Ok(result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tag {TagId}", id);
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error retrieving tag"));
			}
		}

		/// <summary>
		/// Get all tags for a specific device
		/// GET /api/tags/device/{deviceId}
		/// </summary>
		[HttpGet("device/{deviceId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> GetByDeviceId(Guid deviceId)
		{
			try
			{
				_logger.LogInformation("Fetching tags for device: {DeviceId}", deviceId);
				var filter = new TagFilterDto { DeviceId = deviceId };
				var result = await _tagService.GetAllAsync(filter);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tags for device {DeviceId}", deviceId);
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error retrieving tag"));
			}
		}

		/// <summary>
		/// Create new tag with scaling configuration
		/// POST /api/tags
		/// </summary>
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> Create([FromBody] CreateTagDto dto)
		{
			try
			{
				if (!ModelState.IsValid)
					return BadRequest(ModelState);

				_logger.LogInformation("Creating tag: {TagName}", dto.Name);
				var result = await _tagService.CreateAsync(dto);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error creating tag");
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error creating tag"));
			}
		}

		/// <summary>
		/// Update existing tag (scaling, name, etc)
		/// PUT /api/tags/{id}
		/// </summary>
		[HttpPut("{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTagDto dto)
		{
			try
			{
				if (!ModelState.IsValid)
					return BadRequest(ModelState);

				_logger.LogInformation("Updating tag: {TagId}", id);
				var result = await _tagService.UpdateAsync(id, dto);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error updating tag {TagId}", id);
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error updating tag"));
			}
		}

		/// <summary>
		/// Delete tag (soft delete)
		/// DELETE /api/tags/{id}
		/// </summary>
		[HttpDelete("{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> Delete(Guid id)
		{
			try
			{
				_logger.LogInformation("Deleting tag: {TagId}", id);
				var result = await _tagService.DeleteAsync(id);
				if (!result)
					return NotFound(ApiResponse<object>.Fail(404, "Tag tidak ditemukan"));

				return Ok(ApiResponse<object>.SuccessWithStatus(200, null, "Tag berhasil dihapus"));
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error deleting tag {TagId}", id);
				return StatusCode(500, ApiResponse<object>.Fail(500, "Error deleting tag"));
			}
		}
	}
}
