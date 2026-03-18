using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.DTOs.Tag;
using Core.Entities;
using Core.DTOs;

namespace Core.Interface
{
	/// <summary>
	/// Service interface for Tag business logic
	/// Handles validation, linear scaling calculations, and data transformations
	/// Part of Business Logic / Service Layer
	/// </summary>
	public interface ITagService
	{
		/// <summary>
		/// Get all tags with optional filtering and pagination
		/// </summary>
		Task<ApiResponse<List<TagResponseDto>>> GetAllAsync(TagFilterDto filter);

		/// <summary>
		/// Get a single tag with validation
		/// </summary>
		Task<TagResponseDto?> GetByIdAsync(Guid id);

		/// <summary>
		/// Get all tags for a device with pagination
		/// </summary>
		Task<(IEnumerable<TagResponseDto> tags, int total)> GetByDeviceIdAsync(Guid deviceId, int skip = 0, int take = 50);

		/// <summary>
		/// Search tags with optional device filtering and pagination
		/// </summary>
		Task<(IEnumerable<TagResponseDto> tags, int total)> SearchAsync(string? searchTerm = null, Guid? deviceId = null, int skip = 0, int take = 50);

		/// <summary>
		/// Create a new tag with comprehensive validation
		/// </summary>
		Task<TagResponseDto> CreateAsync(CreateTagDto createDto);

		/// <summary>
		/// Update an existing tag with validation
		/// </summary>
		Task<TagResponseDto> UpdateAsync(Guid id, UpdateTagDto updateDto);

		/// <summary>
		/// Delete a tag (soft delete)
		/// </summary>
		Task<bool> DeleteAsync(Guid id);

		/// <summary>
		/// Apply linear scaling transformation
		/// Formula: EU = (Raw - RawMin) * (EuMax - EuMin) / (RawMax - RawMin) + EuMin
		/// </summary>
	Task<double> ApplyLinearScalingAsync(Guid tagId, double rawValue);

	/// <summary>
	/// Reverse linear scaling - convert engineering value back to raw device value
	/// Formula: Raw = (EU - EuMin) * (RawMax - RawMin) / (EuMax - EuMin) + RawMin
	/// </summary>
	Task<double> ReverseScalingAsync(Guid tagId, double euValue);
		Task<(bool isValid, string? errorMessage)> ValidateScalingAsync(CreateTagDto createDto);
	}
}
