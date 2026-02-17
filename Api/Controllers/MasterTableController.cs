using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Core.Interface;
using Core.DTOs.MasterTable;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/master-tables")]
    [Authorize]
    public class MasterTableController : ControllerBase
    {
        private readonly IMasterTableService _masterTableService;

        public MasterTableController(IMasterTableService masterTableService)
        {
            _masterTableService = masterTableService;
        }

        /// <summary>
        /// Get all master tables
        /// GET /api/master-tables
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var result = await _masterTableService.GetAllAsync();
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while retrieving master tables", error = ex.Message });
            }
        }

        /// <summary>
        /// Get a master table by ID
        /// GET /api/master-tables/{id}
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _masterTableService.GetByIdAsync(id);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while retrieving master table", error = ex.Message });
            }
        }

        /// <summary>
        /// Create a new master table
        /// POST /api/master-tables
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateMasterTableDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _masterTableService.CreateAsync(dto);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while creating master table", error = ex.Message });
            }
        }

        /// <summary>
        /// Update a master table
        /// PUT /api/master-tables/{id}
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMasterTableDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _masterTableService.UpdateAsync(id, dto);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while updating master table", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete a master table (soft delete)
        /// DELETE /api/master-tables/{id}
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                var result = await _masterTableService.DeleteAsync(id);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while deleting master table", error = ex.Message });
            }
        }

        // Nested Fields Endpoints

        /// <summary>
        /// Get all fields for a master table
        /// GET /api/master-tables/{masterTableId}/fields
        /// </summary>
        [HttpGet("{masterTableId}/fields")]
        public async Task<IActionResult> GetFields(Guid masterTableId)
        {
            try
            {
                var result = await _masterTableService.GetFieldsAsync(masterTableId);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while retrieving fields", error = ex.Message });
            }
        }

        /// <summary>
        /// Add a new field to a master table
        /// POST /api/master-tables/{masterTableId}/fields
        /// </summary>
        [HttpPost("{masterTableId}/fields")]
        public async Task<IActionResult> CreateField(Guid masterTableId, [FromBody] CreateMasterTableFieldDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _masterTableService.CreateFieldAsync(masterTableId, dto);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while creating field", error = ex.Message });
            }
        }

        /// <summary>
        /// Update a field in a master table
        /// PUT /api/master-tables/{masterTableId}/fields/{fieldId}
        /// </summary>
        [HttpPut("{masterTableId}/fields/{fieldId}")]
        public async Task<IActionResult> UpdateField(Guid masterTableId, Guid fieldId, [FromBody] UpdateMasterTableFieldDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _masterTableService.UpdateFieldAsync(masterTableId, fieldId, dto);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while updating field", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete a field from a master table (soft delete)
        /// DELETE /api/master-tables/{masterTableId}/fields/{fieldId}
        /// </summary>
        [HttpDelete("{masterTableId}/fields/{fieldId}")]
        public async Task<IActionResult> DeleteField(Guid masterTableId, Guid fieldId)
        {
            try
            {
                var result = await _masterTableService.DeleteFieldAsync(masterTableId, fieldId);
                return StatusCode(result.Status, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "An error occurred while deleting field", error = ex.Message });
            }
        }
    }
}
