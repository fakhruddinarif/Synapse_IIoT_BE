using Core.DTOs.Device;
using Core.Entities;

namespace Core.Interface
{
	public interface IDeviceRepository
	{
		Task<Device?> GetByIdAsync(Guid id);
		Task<(List<Device> Devices, int TotalCount)> GetAllAsync(DeviceFilterDto filter);
		Task<Device> CreateAsync(Device device);
		Task<Device?> UpdateAsync(Device device);
		Task<bool> DeleteAsync(Guid id);
		Task<bool> ExistsAsync(Guid id);
		Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
	}
}
