using Core.DTOs.Device;
using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
	public class DeviceRepository : IDeviceRepository
	{
		private readonly AppDbContext _context;

		public DeviceRepository(AppDbContext context)
		{
			_context = context;
		}

		public async Task<Device?> GetByIdAsync(Guid id)
		{
			return await _context.Devices
				.Where(d => d.DeletedAt == null)
				.FirstOrDefaultAsync(d => d.Id == id);
		}

        public async Task<(List<Device> Devices, int TotalCount)> GetAllAsync(DeviceFilterDto filter)
        {
            var query = _context.Devices.Where(d => d.DeletedAt == null);


			if (!string.IsNullOrWhiteSpace(filter.Name))
			{
				var name = filter.Name.ToLower();
				query = query.Where(d => d.Name != null && d.Name.ToLower().Contains(name));
			}

			if (!string.IsNullOrWhiteSpace(filter.Description))
			{
				var desc = filter.Description.ToLower();
				query = query.Where(d => d.Description != null && d.Description.ToLower().Contains(desc));
			}

			if (filter.Protocol.HasValue)
			{
				query = query.Where(d => d.Protocol == filter.Protocol.Value);
			}

			// Search by keyword in Name or Description
			if (!string.IsNullOrWhiteSpace(filter.Search))
			{
				var keyword = filter.Search.ToLower().Trim();
				query = query.Where(d =>
					(d.Name != null && d.Name.ToLower().Contains(keyword)) ||
					(d.Description != null && d.Description.ToLower().Contains(keyword))
				);
			}

            var totalCount = await query.CountAsync();

            var devices = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

            return (devices, totalCount);
        }

		public async Task<Device> CreateAsync(Device device)
		{
			device.Id = Guid.NewGuid();
			device.CreatedAt = DateTime.UtcNow;
			device.UpdatedAt = null;
			device.DeletedAt = null;

			await _context.Devices.AddAsync(device);
			await _context.SaveChangesAsync();

			return device;
		}

		public async Task<Device?> UpdateAsync(Device device)
		{
			var existing = await GetByIdAsync(device.Id);
			if (existing == null) return null;

			device.UpdatedAt = DateTime.UtcNow;
			_context.Devices.Update(device);
			await _context.SaveChangesAsync();

			return device;
		}

		public async Task<bool> DeleteAsync(Guid id)
		{
			var device = await GetByIdAsync(id);
			if (device == null) return false;

			// Soft delete
			device.DeletedAt = DateTime.UtcNow;
			device.UpdatedAt = DateTime.UtcNow;
			_context.Devices.Update(device);
			await _context.SaveChangesAsync();

			return true;
		}

		public async Task<bool> ExistsAsync(Guid id)
		{
			return await _context.Devices
				.Where(d => d.DeletedAt == null)
				.AnyAsync(d => d.Id == id);
		}

		public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null)
		{
			var query = _context.Devices
				.Where(d => d.DeletedAt == null && d.Name == name);

			if (excludeId.HasValue)
			{
				query = query.Where(d => d.Id != excludeId.Value);
			}

			return await query.AnyAsync();
		}
	}
}
