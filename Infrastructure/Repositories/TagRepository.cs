using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Entities;
using Core.Enums;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories
{
	/// <summary>
	/// Repository implementation for Tag operations
	/// Provides data access layer for sensor/input mapping management
	/// Uses Entity Framework Core with soft delete pattern
	/// </summary>
	public class TagRepository : ITagRepository
	{
		private readonly AppDbContext _context;
		private readonly ILogger<TagRepository> _logger;

		public TagRepository(AppDbContext context, ILogger<TagRepository> logger)
		{
			_context = context;
			_logger = logger;
		}

		/// <summary>
		/// Retrieve a single tag by ID (with soft delete filtering)
		/// </summary>
		public async Task<Tag?> GetByIdAsync(Guid id)
		{
			try
			{
				return await _context.Tags
					.AsNoTracking()
					.FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tag {TagId}: {Exception}", id, ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Retrieve all tags for a specific device with pagination
		/// </summary>
		public async Task<IEnumerable<Tag>> GetByDeviceIdAsync(Guid deviceId, int skip = 0, int take = 50)
		{
			try
			{
				return await _context.Tags
					.AsNoTracking()
					.Where(t => t.DeviceId == deviceId && t.DeletedAt == null)
					.OrderBy(t => t.Name)
					.Skip(skip)
					.Take(take)
					.ToListAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tags for device {DeviceId}: {Exception}", deviceId, ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Find a tag by its device memory address (e.g., "40001" for Modbus register)
		/// </summary>
	public async Task<Tag?> GetByAddressAsync(Guid deviceId, string address)
	{
		try
		{
			return await _context.Tags
				.AsNoTracking()
				.FirstOrDefaultAsync(t => 
					t.DeviceId == deviceId && 
					t.Address == address && 
					t.DeletedAt == null);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving tag by address {Address}: {Exception}", address, ex.Message);
			throw;
		}
	}

	/// <summary>
	/// Retrieve all tags with optional device filtering and search term
	/// </summary>
	public async Task<IEnumerable<Tag>> GetAllAsync(Guid? deviceId = null, string? searchTerm = null, int skip = 0, int take = 50)
	{
		try
		{
			var query = _context.Tags
				.AsNoTracking()
				.Where(t => t.DeletedAt == null);

			// Filter by device if specified
			if (deviceId.HasValue)
			{
				query = query.Where(t => t.DeviceId == deviceId.Value);
			}

			// Apply search filter on name or address
			if (!string.IsNullOrEmpty(searchTerm))
			{
				var searchLower = searchTerm.ToLower();
				query = query.Where(t => 
					t.Name.ToLower().Contains(searchLower) || 
					t.Address.ToLower().Contains(searchLower));
			}

			return await query
				.OrderBy(t => t.Name)
				.Skip(skip)
				.Take(take)
				.ToListAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving tags: {Exception}", ex.Message);
			throw;
		}
	}

	/// <summary>
	/// Get total count of tags for pagination calculations
	/// </summary>
	public async Task<int> GetTotalCountAsync(Guid? deviceId = null, string? searchTerm = null)
	{
		try
		{
			var query = _context.Tags
				.Where(t => t.DeletedAt == null);

			if (deviceId.HasValue)
			{
				query = query.Where(t => t.DeviceId == deviceId.Value);
			}

			if (!string.IsNullOrEmpty(searchTerm))
			{
				var searchLower = searchTerm.ToLower();
				query = query.Where(t => 
					t.Name.ToLower().Contains(searchLower) || 
					t.Address.ToLower().Contains(searchLower));
			}

			return await query.CountAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting tag count: {Exception}", ex.Message);
			throw;
		}
	}

	/// <summary>
	/// Create and persist a new tag to the database
	/// </summary>
	public async Task<Tag> CreateAsync(Tag tag)
		{
			try
			{
				// Verify device exists before creating tag
				var deviceExists = await _context.Devices
					.AnyAsync(d => d.Id == tag.DeviceId && d.DeletedAt == null);

				if (!deviceExists)
				{
					throw new InvalidOperationException($"Device {tag.DeviceId} does not exist");
				}

				// Check for duplicate address
				var existingTag = await GetByAddressAsync(tag.DeviceId, tag.Address);
				if (existingTag != null)
				{
					throw new InvalidOperationException($"Tag with address '{tag.Address}' already exists for this device");
				}

				_context.Tags.Add(tag);
				await _context.SaveChangesAsync();

				_logger.LogInformation("Tag created: {TagName} (ID: {TagId}) on device {DeviceId}", tag.Name, tag.Id, tag.DeviceId);
				return tag;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error creating tag: {Exception}", ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Update an existing tag (does not allow modifying device ID)
		/// </summary>
		public async Task<Tag> UpdateAsync(Tag tag)
		{
			try
			{
				var existingTag = await _context.Tags
					.FirstOrDefaultAsync(t => t.Id == tag.Id && t.DeletedAt == null);

				if (existingTag == null)
				{
					throw new KeyNotFoundException($"Tag {tag.Id} not found");
				}

				// Prevent changing device association
				if (existingTag.DeviceId != tag.DeviceId)
				{
					throw new InvalidOperationException("Cannot change the device associated with a tag");
				}

				// Check if new address conflicts with other tags on same device
				var addressConflict = await _context.Tags
					.AnyAsync(t => 
						t.DeviceId == tag.DeviceId && 
						t.Address == tag.Address && 
						t.Id != tag.Id && 
t.DeletedAt == null);

				if (addressConflict)
				{
					throw new InvalidOperationException($"Tag with address '{tag.Address}' already exists for this device");
				}

				// Update properties
				existingTag.Name = tag.Name;
				existingTag.Address = tag.Address;
				existingTag.DataType = tag.DataType;
				existingTag.AccessMode = tag.AccessMode;
				existingTag.Unit = tag.Unit;
				existingTag.RawMin = tag.RawMin;
				existingTag.RawMax = tag.RawMax;
				existingTag.EuMin = tag.EuMin;
				existingTag.EuMax = tag.EuMax;
				existingTag.OpcUaNodeId = tag.OpcUaNodeId;
				existingTag.CreatedAt = DateTime.UtcNow;

				_context.Tags.Update(existingTag);
				await _context.SaveChangesAsync();

				_logger.LogInformation("Tag updated: {TagName} (ID: {TagId})", existingTag.Name, existingTag.Id);
				return existingTag;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error updating tag {TagId}: {Exception}", tag.Id, ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Soft delete a tag by marking it as deleted but preserving historical data
		/// </summary>
	public async Task<bool> DeleteAsync(Guid id)
	{
		try
		{
			var tag = await _context.Tags
				.FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);

			if (tag == null)
			{
				return false;
			}

			tag.DeletedAt = DateTime.UtcNow;
			tag.CreatedAt = DateTime.UtcNow;

			_context.Tags.Update(tag);
			await _context.SaveChangesAsync();

			_logger.LogInformation("Tag soft-deleted: {TagName} (ID: {TagId})", tag.Name, id);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error deleting tag {TagId}", id);
			throw;
		}
	}

	/// <summary>
	/// Check if a tag address exists for a device (used for validation)
	/// Supports excluding a specific tag ID (for update operations)
	/// </summary>
	public async Task<bool> ExistsAsync(Guid deviceId, string address, Guid? excludeId = null)
		{
			try
			{
				var query = _context.Tags
					.Where(t => t.DeviceId == deviceId && t.Address == address && t.DeletedAt == null);

				if (excludeId.HasValue)
				{
					query = query.Where(t => t.Id != excludeId.Value);
				}

				return await query.AnyAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error checking tag existence: {Exception}", ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Retrieve all tags of a specific data type (e.g., all FLOAT tags for calculations)
		/// </summary>
		public async Task<IEnumerable<Tag>> GetByDataTypeAsync(DataType dataType, Guid? deviceId = null)
		{
			try
			{
				var query = _context.Tags
					.AsNoTracking()
					.Where(t => t.DataType == dataType && t.DeletedAt == null);

			if (deviceId.HasValue)
				{
					query = query.Where(t => t.DeviceId == deviceId.Value);
				}

				return await query
					.OrderBy(t => t.Name)
					.ToListAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving tags by data type: {Exception}", ex.Message);
				throw;
			}
		}
	}
}
