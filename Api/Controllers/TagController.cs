using Core.DTOs.Tag;
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
		private readonly ILogger<TagController> _logger;

		public TagController(ITagService tagService, ILogger<TagController> logger)
		{
			_tagService = tagService;
			_logger = logger;
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
				return StatusCode(500, new
				{
					status = 500,
					message = "Error retrieving tags",
					error = ex.Message
				});
			}
		}

		/// <summary>
		/// Get tag by ID with current values
		/// GET /api/tags/{id}
		/// </summary>
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
					return NotFound(new { status = 404, message = "Tag not found" });
				
				return Ok(result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tag {TagId}", id);
				return StatusCode(500, new
				{
					status = 500,
					message = "Error retrieving tag",
					error = ex.Message
				});
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
				return StatusCode(500, new
				{
					status = 500,
					message = "Error retrieving tag",
					error = ex.Message
				});
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
				return StatusCode(500, new
				{
					status = 500,
					message = "Error creating tag",
					error = ex.Message
				});
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
				return StatusCode(500, new
				{
					status = 500,
					message = "Error updating tag",
					error = ex.Message
				});
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
					return NotFound(new { status = 404, message = "Tag not found" });
				
				return Ok(new { status = 200, message = "Tag deleted successfully" });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error deleting tag {TagId}", id);
				return StatusCode(500, new
				{
					status = 500,
					message = "Error deleting tag",
					error = ex.Message
				});
			}
		}
	}
}
