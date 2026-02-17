using Core.DTOs;
using Core.DTOs.MasterTable;

namespace Core.Interface
{
    public interface IMasterTableService
    {
        Task<ApiResponse<List<MasterTableDto>>> GetAllAsync();
        Task<ApiResponse<MasterTableDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<MasterTableDto>> CreateAsync(CreateMasterTableDto dto);
        Task<ApiResponse<MasterTableDto>> UpdateAsync(Guid id, UpdateMasterTableDto dto);
        Task<ApiResponse<object>> DeleteAsync(Guid id);

        // Fields management
        Task<ApiResponse<List<MasterTableFieldDto>>> GetFieldsAsync(Guid masterTableId);
        Task<ApiResponse<MasterTableFieldDto>> CreateFieldAsync(Guid masterTableId, CreateMasterTableFieldDto dto);
        Task<ApiResponse<MasterTableFieldDto>> UpdateFieldAsync(Guid masterTableId, Guid fieldId, UpdateMasterTableFieldDto dto);
        Task<ApiResponse<object>> DeleteFieldAsync(Guid masterTableId, Guid fieldId);
    }
}
