using Core.DTOs.StorageFlow;
using Core.Exceptions;
using Core.Interface;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/storage-flow")]
    public class StorageFlowController : ControllerBase
    {
        private readonly IStorageFlowService _storageFlowService;

        public StorageFlowController(IStorageFlowService storageFlowService)
        {
            _storageFlowService = storageFlowService;
        }

        /// <summary>
        /// Get all storage flows
        /// GET /api/storage-flow
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var flows = await _storageFlowService.GetAllAsync();
                return Ok(new { status = 200, message = "Storage flows retrieved successfully", data = flows });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }

        /// <summary>
        /// Get storage flow by ID
        /// GET /api/storage-flow/{id}
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var flow = await _storageFlowService.GetByIdAsync(id);
                return Ok(new { status = 200, message = "Storage flow retrieved successfully", data = flow });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { status = 404, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }

        /// <summary>
        /// Create a new storage flow
        /// POST /api/storage-flow
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateStorageFlowDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { status = 400, message = "Validation failed", errors = ModelState });
                }

                var flow = await _storageFlowService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = flow.Id }, new { status = 201, message = "Storage flow created successfully", data = flow });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { status = 404, message = ex.Message });
            }
            catch (BadRequestException ex)
            {
                return BadRequest(new { status = 400, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing storage flow
        /// PUT /api/storage-flow/{id}
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStorageFlowDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { status = 400, message = "Validation failed", errors = ModelState });
                }

                var flow = await _storageFlowService.UpdateAsync(id, dto);
                return Ok(new { status = 200, message = "Storage flow updated successfully", data = flow });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { status = 404, message = ex.Message });
            }
            catch (BadRequestException ex)
            {
                return BadRequest(new { status = 400, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete a storage flow (soft delete)
        /// DELETE /api/storage-flow/{id}
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                var result = await _storageFlowService.DeleteAsync(id);

                if (!result)
                {
                    return NotFound(new { status = 404, message = "Storage flow not found" });
                }

                return Ok(new { status = 200, message = "Storage flow deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }

        /// <summary>
        /// Discover available fields from a device
        /// POST /api/storage-flow/discover-fields
        /// </summary>
        [HttpPost("discover-fields")]
        public async Task<IActionResult> DiscoverFields([FromBody] DiscoverFieldsRequestDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { status = 400, message = "Validation failed", errors = ModelState });
                }

                var fields = await _storageFlowService.DiscoverFieldsAsync(dto.DeviceId);
                return Ok(new { status = 200, message = "Fields discovered successfully", data = fields });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { status = 404, message = ex.Message });
            }
            catch (BadRequestException ex)
            {
                return BadRequest(new { status = 400, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred", error = ex.Message });
            }
        }
    }
}
