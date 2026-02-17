using Core.Entities;

namespace Core.Interface
{
    public interface IMasterTableRepository
    {
        Task<IEnumerable<MasterTable>> GetAllAsync();
        Task<MasterTable?> GetByIdAsync(Guid id);
        Task<MasterTable?> GetByTableNameAsync(string tableName);
        Task<MasterTable> CreateAsync(MasterTable masterTable);
        Task<MasterTable> UpdateAsync(MasterTable masterTable);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
        Task<bool> TableNameExistsAsync(string tableName, Guid? excludeId = null);
    }
}
