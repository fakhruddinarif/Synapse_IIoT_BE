using Core.DTOs;
using Core.DTOs.Device;

namespace Core.Interface
{
	public interface IDeviceService
	{
		Task<ApiResponse<DeviceResponseDto>> GetByIdAsync(Guid id);
		Task<ApiResponse<List<DeviceResponseDto>>> GetAllAsync(DeviceFilterDto filter);
		Task<ApiResponse<DeviceResponseDto>> CreateAsync(CreateDeviceDto dto);
		Task<ApiResponse<DeviceResponseDto>> UpdateAsync(Guid id, UpdateDeviceDto dto);
		Task<ApiResponse<object>> DeleteAsync(Guid id);
		Task<ApiResponse<TestHttpConnectionResponseDto>> TestHttpConnectionAsync(TestHttpRequestDto request);
	}
}
