using Core.Entities;

namespace Core.Interface
{
    public interface IMasterTableFieldsRepository
    {
        Task<IEnumerable<MasterTableFields>> GetByMasterTableIdAsync(Guid masterTableId);
        Task<MasterTableFields?> GetByIdAsync(Guid id);
        Task<MasterTableFields> CreateAsync(MasterTableFields field);
        Task<MasterTableFields> UpdateAsync(MasterTableFields field);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
    }
}
