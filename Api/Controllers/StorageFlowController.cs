using Core.DTOs.StorageFlow;
using Core.Exceptions;
using Core.DTOs;
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
                return Ok(ApiResponse<object>.SuccessWithStatus(200, flows, "Daftar storage flow berhasil diambil"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
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
                return Ok(ApiResponse<object>.SuccessWithStatus(200, flow, "Storage flow berhasil diambil"));
            }
            catch (NotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(404, ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
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
                    return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid",
                    ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
                        .Where(m => !string.IsNullOrWhiteSpace(m)).ToList()));
                }

                var flow = await _storageFlowService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = flow.Id }, ApiResponse<object>.SuccessWithStatus(201, flow, "Storage flow berhasil dibuat"));
            }
            catch (NotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(404, ex.Message));
            }
            catch (BadRequestException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(400, ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
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
                    return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid",
                    ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
                        .Where(m => !string.IsNullOrWhiteSpace(m)).ToList()));
                }

                var flow = await _storageFlowService.UpdateAsync(id, dto);
                return Ok(ApiResponse<object>.SuccessWithStatus(200, flow, "Storage flow berhasil diperbarui"));
            }
            catch (NotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(404, ex.Message));
            }
            catch (BadRequestException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(400, ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
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
                    return NotFound(ApiResponse<object>.Fail(404, "Storage flow tidak ditemukan"));
                }

                return Ok(ApiResponse<object>.SuccessWithStatus(200, null, "Storage flow berhasil dihapus"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
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
                    return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid",
                    ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
                        .Where(m => !string.IsNullOrWhiteSpace(m)).ToList()));
                }

                var fields = await _storageFlowService.DiscoverFieldsAsync(dto.DeviceId);
                return Ok(ApiResponse<object>.SuccessWithStatus(200, fields, "Path berhasil dideteksi"));
            }
            catch (NotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(404, ex.Message));
            }
            catch (BadRequestException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(400, ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memproses permintaan"));
            }
        }
    }
}
