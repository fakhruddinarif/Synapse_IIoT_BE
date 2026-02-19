using Core.DTOs.StorageFlow;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IStorageFlowService
    {
        Task<StorageFlowResponseDto> GetByIdAsync(Guid id);
        Task<IEnumerable<StorageFlowResponseDto>> GetAllAsync();
        Task<StorageFlowResponseDto> CreateAsync(CreateStorageFlowDto dto);
        Task<StorageFlowResponseDto> UpdateAsync(Guid id, UpdateStorageFlowDto dto);
        Task<bool> DeleteAsync(Guid id);
        Task<List<DiscoveredFieldDto>> DiscoverFieldsAsync(Guid deviceId);
    }
}
