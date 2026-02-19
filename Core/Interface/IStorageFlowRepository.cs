using Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IStorageFlowRepository
    {
        Task<StorageFlow?> GetByIdAsync(Guid id, bool includeRelations = false);
        Task<IEnumerable<StorageFlow>> GetAllAsync(bool includeDeleted = false, bool includeRelations = false);
        Task<IEnumerable<StorageFlow>> GetActiveFlowsAsync();
        Task<StorageFlow> CreateAsync(StorageFlow storageFlow);
        Task<StorageFlow> UpdateAsync(StorageFlow storageFlow);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
        Task<bool> IsTableNameInUseAsync(Guid masterTableId, Guid? excludeFlowId = null);
    }
}
