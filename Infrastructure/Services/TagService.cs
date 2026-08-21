using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.DTOs;
using Core.DTOs.Tag;
using Core.Entities;
using Core.Enums;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
	/// <summary>
	/// Service implementation for Tag business logic
	/// Handles linear scaling calculations, validation, and data transformations
	/// Key features:
	/// - Linear interpolation: Raw value range (RawMin-RawMax) → Engineering Units (EuMin-EuMax)
	/// - Data type conversion between different sensor value types
	/// - Address duplication prevention
	/// - Scaling parameter validation
	/// </summary>
	public class TagService : ITagService
	{
		private readonly ITagRepository _tagRepository;
		private readonly IDeviceRepository _deviceRepository;
		private readonly ILogger<TagService> _logger;

		private readonly IAcquisitionControl _acquisition;

		public TagService(
			ITagRepository tagRepository,
			IDeviceRepository deviceRepository,
			ILogger<TagService> logger,
			IAcquisitionControl acquisition)
		{
			_tagRepository = tagRepository;
			_deviceRepository = deviceRepository;
			_logger = logger;
			_acquisition = acquisition;
		}

		/// <summary>
		/// Memberi tahu penjadwal bahwa rencana akuisisi berubah.
		///
		/// Dipanggil setelah perubahan TERSIMPAN, bukan sebelum: penjadwal membaca ulang
		/// konfigurasi dari database, jadi sinyal yang dikirim lebih dulu akan membaca
		/// keadaan lama dan perubahannya baru terpakai pada replan berikutnya — yang mungkin
		/// tidak pernah datang.
		///
		/// Pemanggilan berkali-kali tidak masalah: sinyalnya digabung, jadi membuat 200 tag
		/// lewat endpoint massal menghasilkan satu penyusunan ulang, bukan dua ratus.
		/// </summary>
		private void NotifyAcquisition(string reason)
		{
			try
			{
				_acquisition.RequestReload(reason);
			}
			catch (Exception ex)
			{
				// Tag sudah tersimpan. Gagal memberi tahu penjadwal berarti perubahan baru
				// aktif pada replan berikutnya, bukan berarti permintaan pengguna gagal —
				// jadi ini dicatat, tidak dilempar ke pemanggil.
				_logger.LogWarning(ex, "Gagal memberi sinyal replan ke penjadwal akuisisi");
			}
		}

		/// <summary>
		/// Retrieve tag details for display with data validation
		/// </summary>
	public async Task<TagResponseDto?> GetByIdAsync(Guid id)
	{
		try
		{
			var tag = await _tagRepository.GetByIdAsync(id);
			if (tag == null)
				return null;

			return ToDto(tag);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting tag {TagId}", id);
			throw;
		}
	}

	/// <summary>
	/// Get all tags for a device with pagination
	/// </summary>
		public async Task<(IEnumerable<TagResponseDto> tags, int total)> GetByDeviceIdAsync(
		Guid deviceId,
		int skip = 0,
		int take = 50)
	{
		try
		{
			// Verify device exists
			var device = await _deviceRepository.GetByIdAsync(deviceId);
			if (device == null)
				throw new KeyNotFoundException($"Device {deviceId} not found");

			var tags = await _tagRepository.GetByDeviceIdAsync(deviceId, skip, take);
			var total = await _tagRepository.GetTotalCountAsync(deviceId);

			var dtos = tags.Select(ToDto).ToList();
			return (dtos, total);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting tags for device {DeviceId}", deviceId);
			throw;
		}
	}
		/// Search tags with optional device filtering and pagination
		/// </summary>
		public async Task<(IEnumerable<TagResponseDto> tags, int total)> SearchAsync(
			string? searchTerm = null,
		Guid? deviceId = null,
		int skip = 0,
		int take = 50)
	{
		try
		{
			var tags = await _tagRepository.GetAllAsync(deviceId, searchTerm, skip, take);
			var total = await _tagRepository.GetTotalCountAsync(deviceId, searchTerm);

			var dtos = tags.Select(ToDto).ToList();
			return (dtos, total);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error searching tags");
			throw;
		}
	}

	/// <summary>
	/// Get all tags with optional filtering and pagination
	/// </summary>
	public async Task<ApiResponse<List<TagResponseDto>>> GetAllAsync(TagFilterDto filter)
	{
		try
		{
			var skip = (filter.Page - 1) * filter.PageSize;
			var tags = await _tagRepository.GetAllAsync(filter.DeviceId, filter.SearchTerm, skip, filter.PageSize);
			var total = await _tagRepository.GetTotalCountAsync(filter.DeviceId, filter.SearchTerm);

			var dtos = tags.Select(ToDto).ToList();
			var totalPages = (int)Math.Ceiling((double)total / filter.PageSize);

			return new ApiResponse<List<TagResponseDto>>
			{
				Status = 200,
				Message = "Success",
				Data = dtos,
				Paging = new PagingInfo
				{
					Size = filter.PageSize,
					Page = filter.Page,
					TotalPage = totalPages,
					TotalItem = total
				}
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving all tags");
			// Detail exception hanya masuk log (sudah dicatat di atas); klien menerima pesan
			// umum. Pesan exception database memuat potongan SQL dan nama kolom.
			return ApiResponse<List<TagResponseDto>>.Fail(500, "Gagal mengambil daftar tag");
		}
	}

	/// <summary>
	/// Create a new tag with comprehensive validation
	/// </summary>
	/// <summary>
	/// Penskalaan dianggap dimaksudkan bila rentang mentah berbeda dari rentang teknis.
	/// Raw 0..100 → EU 0..100 adalah pemetaan identitas; menandainya "berskala" hanya
	/// menambah perhitungan yang hasilnya sama.
	/// </summary>
	private static bool IsScalingMeaningful(double rawMin, double rawMax, double euMin, double euMax)
	{
		const double tolerance = 1e-9;
		if (Math.Abs(rawMax - rawMin) < tolerance) return false;
		return Math.Abs(rawMin - euMin) > tolerance || Math.Abs(rawMax - euMax) > tolerance;
	}

	/// <summary>
	/// Membuat banyak tag dalam satu permintaan — jalur yang dipakai pemilih key di UI.
	///
	/// Tag yang gagal TIDAK menggagalkan seluruh batch: satu path yang sudah dipakai tag lain
	/// tidak boleh membuang sebelas pilihan lain yang sah. Yang ditolak dilaporkan beserta
	/// alasannya, supaya pengguna tahu tepat mana yang perlu diperbaiki.
	/// </summary>
	public async Task<ApiResponse<BulkTagResultDto>> CreateBulkAsync(CreateTagsBulkDto bulkDto)
	{
		var device = await _deviceRepository.GetByIdAsync(bulkDto.DeviceId);
		if (device == null)
		{
			return ApiResponse<BulkTagResultDto>.Fail(404, "Perangkat tidak ditemukan");
		}

		var result = new BulkTagResultDto();
		var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var dto in bulkDto.Tags)
		{
			dto.DeviceId = bulkDto.DeviceId;

			// Duplikat DI DALAM satu batch juga harus ditangkap: pengguna bisa memilih key yang
			// sama dua kali lewat penanganan array, dan repositori belum melihat yang pertama
			// sampai transaksinya selesai.
			var key = (dto.SourceTopic ?? string.Empty) + "|" + dto.Address;
			if (!seenAddresses.Add(key))
			{
				result.Skipped++;
				result.Rejected.Add($"{dto.Name}: path {dto.Address} muncul dua kali dalam permintaan ini");
				continue;
			}

			try
			{
				var created = await CreateAsync(dto);
				result.Created++;
				result.Tags.Add(created);
			}
			catch (Exception ex)
			{
				result.Skipped++;
				result.Rejected.Add($"{dto.Name}: {ex.Message}");
			}
		}

		var message = result.Skipped == 0
			? $"{result.Created} tag berhasil dibuat"
			: $"{result.Created} tag dibuat, {result.Skipped} dilewati";

		return result.Created == 0 && result.Skipped > 0
			? ApiResponse<BulkTagResultDto>.FailWithData(400, "Tidak ada tag yang bisa dibuat", result, result.Rejected)
			: ApiResponse<BulkTagResultDto>.Success(result, message);
	}

	public async Task<TagResponseDto> CreateAsync(CreateTagDto createDto)
	{
		try
		{
			// Validate scaling parameters
			var (isScalingValid, scalingError) = await ValidateScalingAsync(createDto);
			if (!isScalingValid)
				throw new InvalidOperationException(scalingError ?? "Invalid scaling parameters");

			// Check device exists
			var device = await _deviceRepository.GetByIdAsync(createDto.DeviceId);
			if (device == null)
				throw new KeyNotFoundException($"Device {createDto.DeviceId} not found");

			// Check address availability
			var addressExists = await _tagRepository.ExistsAsync(createDto.DeviceId, createDto.Address);
			if (addressExists)
				throw new InvalidOperationException($"Tag with address '{createDto.Address}' already exists for this device");

			// Create tag entity
			var tagEntity = new Tag
			{
				DeviceId = createDto.DeviceId,
				Name = createDto.Name,
				Address = createDto.Address,
				DataType = createDto.DataType,
				AccessMode = createDto.AccessMode,
			Unit = createDto.Unit ?? string.Empty,
			RawMin = createDto.RawMin,
			RawMax = createDto.RawMax,
			EuMin = createDto.EuMin,
			EuMax = createDto.EuMax,
			OpcUaNodeId = createDto.OpcUaNodeId,
			SourceTopic = createDto.SourceTopic,
			ScanIntervalMs = createDto.ScanIntervalMs,
			StoreMode = createDto.StoreMode,
			DeadbandAbs = createDto.DeadbandAbs,
			DeadbandPct = createDto.DeadbandPct,
			MaxStoreGapMs = createDto.MaxStoreGapMs,
			// IsScaled diturunkan, bukan diminta dari klien.
			//
			// Sebelumnya kolom ini TIDAK PERNAH di-set sama sekali, sehingga tetap false untuk
			// setiap tag yang dibuat lewat API — rawMin/rawMax/euMin/euMax yang diisi operator
			// tersimpan rapi tapi tidak pernah dipakai menghitung apa pun. Gejalanya: nilai di
			// dasbor sama dengan nilai mentah PLC, dan tidak ada satu pun error.
			IsScaled = IsScalingMeaningful(createDto.RawMin, createDto.RawMax, createDto.EuMin, createDto.EuMax),
			CreatedAt = DateTime.UtcNow
		};

		var createdTag = await _tagRepository.CreateAsync(tagEntity);

			_logger.LogInformation("Tag created: {TagName} (ID: {TagId}) on device {DeviceId}", createdTag.Name, createdTag.Id, createdTag.DeviceId);
			NotifyAcquisition($"tag {createdTag.Id} dibuat");
			return ToDto(createdTag);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating tag");
			throw;
		}
	}

	/// <summary>
	/// Update an existing tag with validation
	/// </summary>
	public async Task<TagResponseDto> UpdateAsync(Guid id, UpdateTagDto updateDto)
	{
		try
		{
			// Get existing tag
			var existingTag = await _tagRepository.GetByIdAsync(id);
			if (existingTag == null)
				throw new KeyNotFoundException($"Tag {id} not found");

			// If scaling parameters changed, validate them
			if (updateDto.RawMin.HasValue || updateDto.RawMax.HasValue ||
				updateDto.EuMin.HasValue || updateDto.EuMax.HasValue)
			{
				var validateDto = new CreateTagDto
				{
					DeviceId = existingTag.DeviceId,
					Name = updateDto.Name ?? existingTag.Name,
					Address = updateDto.Address ?? existingTag.Address,
					DataType = updateDto.DataType ?? existingTag.DataType,
					AccessMode = updateDto.AccessMode ?? existingTag.AccessMode,
					Unit = updateDto.Unit ?? existingTag.Unit ?? string.Empty,
					RawMin = updateDto.RawMin ?? existingTag.RawMin ?? 0,
					RawMax = updateDto.RawMax ?? existingTag.RawMax ?? 4095,
					EuMin = updateDto.EuMin ?? existingTag.EuMin ?? 0,
					EuMax = updateDto.EuMax ?? existingTag.EuMax ?? 100,
					OpcUaNodeId = updateDto.OpcUaNodeId ?? existingTag.OpcUaNodeId
				};

				var (isValid, error) = await ValidateScalingAsync(validateDto);
				if (!isValid)
					throw new InvalidOperationException(error ?? "Invalid scaling parameters");
			}

			// Check address uniqueness if changed
			if (!string.IsNullOrEmpty(updateDto.Address) && updateDto.Address != existingTag.Address)
			{
				var addressExists = await _tagRepository.ExistsAsync(existingTag.DeviceId, updateDto.Address, id);
				if (addressExists)
					throw new InvalidOperationException($"Tag with address '{updateDto.Address}' already exists for this device");
			}

		// Update tag properties
		var tagToUpdate = new Tag
		{
			Id = id,
			DeviceId = existingTag.DeviceId,
			Name = updateDto.Name ?? existingTag.Name,
			Address = updateDto.Address ?? existingTag.Address,
			DataType = updateDto.DataType ?? existingTag.DataType,
			AccessMode = updateDto.AccessMode ?? existingTag.AccessMode,
			Unit = updateDto.Unit ?? existingTag.Unit ?? string.Empty,
			RawMin = updateDto.RawMin ?? existingTag.RawMin,
			RawMax = updateDto.RawMax ?? existingTag.RawMax,
			EuMin = updateDto.EuMin ?? existingTag.EuMin,
			EuMax = updateDto.EuMax ?? existingTag.EuMax,
			OpcUaNodeId = updateDto.OpcUaNodeId ?? existingTag.OpcUaNodeId,
			CreatedAt = existingTag.CreatedAt
		};

		var updatedTag = await _tagRepository.UpdateAsync(tagToUpdate);

		_logger.LogInformation("Tag updated: {TagName} (ID: {TagId})", updatedTag.Name, updatedTag.Id);
		NotifyAcquisition($"tag {updatedTag.Id} diubah");
		return ToDto(updatedTag);
	}
	catch (Exception ex)
	{
		_logger.LogError(ex, "Error updating tag {TagId}", id);
		throw;
	}
}

	/// <summary>
	/// Delete a tag (soft delete)
	/// </summary>
	public async Task<bool> DeleteAsync(Guid id)
	{
		try
			{
				var deleted = await _tagRepository.DeleteAsync(id);
				if (deleted)
				{
					_logger.LogInformation("Tag soft-deleted: {TagId}", id);
					NotifyAcquisition($"tag {id} dihapus");
				}
				return deleted;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error deleting tag {TagId}", id);
				throw;
			}
		}

		/// <summary>
		/// Apply linear scaling transformation
		/// Formula: EU = (Raw - RawMin) * (EuMax - EuMin) / (RawMax - RawMin) + EuMin
		/// Example: Raw=2048 (50% of 0-4095) → EU=50°C (50% of 0-100°C)
		/// </summary>
		public async Task<double> ApplyLinearScalingAsync(Guid tagId, double rawValue)
		{
			try
			{
				var tag = await _tagRepository.GetByIdAsync(tagId);
				if (tag == null)
					throw new KeyNotFoundException($"Tag {tagId} not found");

			var rawMin = tag.RawMin ?? 0;
			var rawMax = tag.RawMax ?? 4095;
			var euMin = tag.EuMin ?? 0;
			var euMax = tag.EuMax ?? 100;

			// Clamp raw value to valid range
			var clampedRaw = Math.Max(rawMin, Math.Min(rawMax, rawValue));

			// Prevent division by zero (use epsilon for floating point comparison)
			const double epsilon = 0.001;
			if (Math.Abs(rawMax - rawMin) < epsilon)
			{
				_logger.LogWarning("Tag {TagId} has same RawMin and RawMax, returning EuMin", tagId);
				return euMin;
			}

			// Linear interpolation formula
			var euValue = ((clampedRaw - rawMin) * (euMax - euMin)) /
						  (rawMax - rawMin) + euMin;

			return Math.Round(euValue, 2); // Round to 2 decimal places for precision
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error applying scaling for tag {TagId}", tagId);
			throw;
		}
	}

	/// <summary>
	/// Reverse linear scaling - convert engineering value back to raw device value
	/// Formula: Raw = (EU - EuMin) * (RawMax - RawMin) / (EuMax - EuMin) + RawMin
	/// Used for writing values to device
	/// </summary>
	public async Task<double> ReverseScalingAsync(Guid tagId, double euValue)
	{
		try
		{
			var tag = await _tagRepository.GetByIdAsync(tagId);
			if (tag == null)
				throw new KeyNotFoundException($"Tag {tagId} not found");

			var rawMin = tag.RawMin ?? 0;
			var rawMax = tag.RawMax ?? 4095;
			var euMin = tag.EuMin ?? 0;
			var euMax = tag.EuMax ?? 100;

			// Check if value is within engineering unit range
			if (euValue < euMin || euValue > euMax)
			{
				_logger.LogWarning("Tag {TagId} EU value {EUValue} outside range [{EuMin}-{EuMax}]", tagId, euValue, euMin, euMax);
			}

			// Prevent division by zero (use epsilon for floating point comparison)
			const double epsilon = 0.001;
			if (Math.Abs(euMax - euMin) < epsilon)
			{
				_logger.LogWarning("Tag {TagId} has same EuMin and EuMax, returning RawMin", tagId);
				return rawMin;
			}

			// Reverse linear interpolation formula
			var rawValue = ((euValue - euMin) * (rawMax - rawMin)) /
						   (euMax - euMin) + rawMin;

			// Clamp to valid raw range
			return Math.Round(Math.Max(rawMin, Math.Min(rawMax, rawValue)), 2);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error reversing scaling for tag {TagId}", tagId);
			throw;
		}
	}

	/// <summary>
	/// Validate scaling parameters before creation/update
	/// Ensures mathematical validity and prevents runtime errors
	/// </summary>
	public async Task<(bool isValid, string? errorMessage)> ValidateScalingAsync(CreateTagDto createDto)
	{
		try
		{
			// For update operation, may have null values - skip optional validation
			// For create operation, has default values - validate them

			if (createDto.RawMin >= createDto.RawMax)
				return (false, "RawMin must be less than RawMax");

			if (createDto.EuMin >= createDto.EuMax)
				return (false, "EuMin must be less than EuMax");

			// Check for reasonable ranges (prevent extreme scaling)
			if (Math.Abs(createDto.RawMax - createDto.RawMin) < 0.001)
				return (false, "Raw value range too small (minimum 0.001)");

			if (Math.Abs(createDto.EuMax - createDto.EuMin) < 0.001)
				return (false, "Engineering unit range too small (minimum 0.001)");

			return (true, null);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error validating scaling");
			return (false, $"Validation error: {ex.Message}");
		}
	}

	/// <summary>
	/// Check if a tag address is available for a device
	/// </summary>
	public async Task<bool> IsAddressAvailableAsync(Guid deviceId, string address, Guid? excludeTagId = null)
	{
		try
		{
			return !await _tagRepository.ExistsAsync(deviceId, address, excludeTagId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error checking address availability for device {DeviceId}", deviceId);
			return false;
		}
	}

	/// <summary>
	/// Convert numeric value to proper CLR type based on DataType enum
	/// </summary>
	public object? ConvertToDataType(double value, DataType dataType)
	{
		try
		{
			const double epsilon = 0.001;
			return dataType switch
			{
				DataType.BOOLEAN => Math.Abs(value) > epsilon, // Value > epsilon threshold = true
				DataType.INT16 => (short)Math.Round(value),
				DataType.INT32 => (int)Math.Round(value),
				DataType.UINT16 => (ushort)Math.Round(Math.Max(0, value)),
				DataType.UINT32 => (uint)Math.Round(Math.Max(0, value)),
				DataType.FLOAT => (float)value,
				DataType.STRING => value.ToString("F2"), // 2 decimal places
				_ => value
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error converting value to {DataType}", dataType);
			return null;
		}
	}

	/// <summary>
	/// Parse string value to proper CLR type based on DataType enum
	/// </summary>
	public object? ParseStringToDataType(string value, DataType dataType)
	{
		try
		{
			return dataType switch
			{
				DataType.BOOLEAN => bool.Parse(value),
				DataType.INT16 => short.Parse(value),
				DataType.INT32 => int.Parse(value),
				DataType.UINT16 => ushort.Parse(value),
				DataType.UINT32 => uint.Parse(value),
				DataType.FLOAT => float.Parse(value),
				DataType.STRING => value,
				_ => value
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error parsing '{Value}' to {DataType}", value, dataType);
			return null;
		}
	}

	/// <summary>
	/// Convert domain entity to DTO for API responses
	/// </summary>
	private static TagResponseDto ToDto(Tag tag)
	{
		var rawMin = tag.RawMin ?? 0;
		var rawMax = tag.RawMax ?? 4095;
		var euMin = tag.EuMin ?? 0;
		var euMax = tag.EuMax ?? 100;
		var scalingFactor = Math.Abs(rawMax - rawMin) > 0.001 ? (euMax - euMin) / (rawMax - rawMin) : 0;

		return new TagResponseDto
		{
			Id = tag.Id,
			DeviceId = tag.DeviceId,
			Name = tag.Name,
			Address = tag.Address,
			DataType = tag.DataType,
			AccessMode = tag.AccessMode,
			Unit = tag.Unit ?? string.Empty,
			RawMin = rawMin,
			RawMax = rawMax,
			EuMin = euMin,
			EuMax = euMax,
			OpcUaNodeId = tag.OpcUaNodeId,
			CreatedAt = tag.CreatedAt,
			IsDeleted = tag.DeletedAt.HasValue,
			ScalingFactor = scalingFactor
		};
	}
    }
}
