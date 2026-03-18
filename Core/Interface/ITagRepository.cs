using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Entities;
using Core.Enums;

namespace Core.Interface
{
	/// <summary>
	/// Repository interface for Tag operations
	/// Handles communication between service layer and database for sensor/input mappings
	/// Part of Data Access Layer with Repository Pattern
	/// </summary>
	public interface ITagRepository
	{
		/// <summary>
		/// Get a single tag by ID
		/// </summary>
	Task<Tag?> GetByIdAsync(Guid id);

	/// <summary>
	/// Get all tags by device ID
	/// </summary>
	Task<IEnumerable<Tag>> GetByDeviceIdAsync(Guid deviceId, int skip = 0, int take = 50);
/// <summary>
	/// Search tags by address within a device
	/// </summary>
	Task<Tag?> GetByAddressAsync(Guid deviceId, string address);

	/// <summary>
	/// Get all tags with optional filtering
	/// </summary>
	Task<IEnumerable<Tag>> GetAllAsync(Guid? deviceId = null, string? searchTerm = null, int skip = 0, int take = 50);

	/// <summary>
	/// Get total count of tags for pagination
	/// </summary>
	Task<int> GetTotalCountAsync(Guid? deviceId = null, string? searchTerm = null);

		/// <summary>
		/// Create a new tag
		/// </summary>
		Task<Tag> CreateAsync(Tag tag);

		/// <summary>
		/// Update an existing tag
		/// </summary>
		Task<Tag> UpdateAsync(Tag tag);

		/// <summary>
		/// Delete a tag (soft delete)
		/// </summary>
		Task<bool> DeleteAsync(Guid id);

		/// <summary>
		/// Check if a tag address already exists for a device (for duplicate prevention)
		/// </summary>
		Task<bool> ExistsAsync(Guid deviceId, string address, Guid? excludeId = null);

		/// <summary>
		/// Get tags by data type (for filtering operations)
		/// </summary>
		Task<IEnumerable<Tag>> GetByDataTypeAsync(DataType dataType, Guid? deviceId = null);
	}
}
