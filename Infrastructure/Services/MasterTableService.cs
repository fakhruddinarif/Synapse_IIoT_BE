using Core.DTOs;
using Core.DTOs.MasterTable;
using Core.Entities;
using Core.Enums;
using Core.Exceptions;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Infrastructure.Services
{
    public class MasterTableService : IMasterTableService
    {
        private readonly IMasterTableRepository _masterTableRepository;
        private readonly IMasterTableFieldsRepository _fieldsRepository;
        private readonly AppDbContext _dbContext;

        private const string MasterTableNotFound = "Master table not found";
        private const string FieldNotFound = "Field not found";

        public MasterTableService(
            IMasterTableRepository masterTableRepository,
            IMasterTableFieldsRepository fieldsRepository,
            AppDbContext dbContext)
        {
            _masterTableRepository = masterTableRepository;
            _fieldsRepository = fieldsRepository;
            _dbContext = dbContext;
        }

        public async Task<ApiResponse<List<MasterTableDto>>> GetAllAsync()
        {
            var masterTables = await _masterTableRepository.GetAllAsync();
            var dtos = masterTables.Select(MapToDto).ToList();
            return ApiResponse<List<MasterTableDto>>.Success(dtos, "Master tables retrieved successfully");
        }

        public async Task<ApiResponse<MasterTableDto>> GetByIdAsync(Guid id)
        {
            var masterTable = await _masterTableRepository.GetByIdAsync(id);
            if (masterTable == null)
            {
                return ApiResponse<MasterTableDto>.Fail(404, MasterTableNotFound);
            }
            return ApiResponse<MasterTableDto>.Success(MapToDto(masterTable));
        }

        public async Task<ApiResponse<MasterTableDto>> CreateAsync(CreateMasterTableDto dto)
        {
            // Validate table name uniqueness
            if (await _masterTableRepository.TableNameExistsAsync(dto.TableName))
            {
                return ApiResponse<MasterTableDto>.Fail(400, $"Table name '{dto.TableName}' already exists");
            }

            var masterTable = new MasterTable
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                TableName = dto.TableName,
                Description = dto.Description,
                IsActive = dto.IsActive,
                Fields = dto.Fields.Select(f => new MasterTableFields
                {
                    Id = Guid.NewGuid(),
                    Name = f.Name,
                    DataType = f.DataType,
                    IsEnabled = f.IsEnabled
                }).ToList()
            };

            // Create metadata record
            var created = await _masterTableRepository.CreateAsync(masterTable);

            // Create physical table in database
            await CreatePhysicalTableAsync(dto.TableName, masterTable.Fields);

            return ApiResponse<MasterTableDto>.SuccessWithStatus(201, MapToDto(created), "Master table created successfully");
        }

        public async Task<ApiResponse<MasterTableDto>> UpdateAsync(Guid id, UpdateMasterTableDto dto)
        {
            var masterTable = await _masterTableRepository.GetByIdAsync(id);
            if (masterTable == null)
            {
                return ApiResponse<MasterTableDto>.Fail(404, MasterTableNotFound);
            }

            // Validate table name uniqueness if changed
            if (dto.TableName != null && dto.TableName != masterTable.TableName)
            {
                if (await _masterTableRepository.TableNameExistsAsync(dto.TableName, id))
                {
                    return ApiResponse<MasterTableDto>.Fail(400, $"Table name '{dto.TableName}' already exists");
                }
                masterTable.TableName = dto.TableName;
            }

            if (dto.Name != null)
                masterTable.Name = dto.Name;

            if (dto.Description != null)
                masterTable.Description = dto.Description;

            if (dto.IsActive.HasValue)
                masterTable.IsActive = dto.IsActive.Value;

            var updated = await _masterTableRepository.UpdateAsync(masterTable);
            return ApiResponse<MasterTableDto>.Success(MapToDto(updated), "Master table updated successfully");
        }

        public async Task<ApiResponse<object>> DeleteAsync(Guid id)
        {
            var exists = await _masterTableRepository.ExistsAsync(id);
            if (!exists)
            {
                return ApiResponse<object>.Fail(404, MasterTableNotFound);
            }

            await _masterTableRepository.DeleteAsync(id);
            return ApiResponse<object>.Success(null, "Master table deleted successfully");
        }

        // Fields management
        public async Task<ApiResponse<List<MasterTableFieldDto>>> GetFieldsAsync(Guid masterTableId)
        {
            var masterTable = await _masterTableRepository.GetByIdAsync(masterTableId);
            if (masterTable == null)
            {
                return ApiResponse<List<MasterTableFieldDto>>.Fail(404, MasterTableNotFound);
            }

            var fields = await _fieldsRepository.GetByMasterTableIdAsync(masterTableId);
            var dtos = fields.Select(MapFieldToDto).ToList();
            return ApiResponse<List<MasterTableFieldDto>>.Success(dtos, "Fields retrieved successfully");
        }

        public async Task<ApiResponse<MasterTableFieldDto>> CreateFieldAsync(Guid masterTableId, CreateMasterTableFieldDto dto)
        {
            var masterTable = await _masterTableRepository.GetByIdAsync(masterTableId);
            if (masterTable == null)
            {
                return ApiResponse<MasterTableFieldDto>.Fail(404, MasterTableNotFound);
            }

            var field = new MasterTableFields
            {
                Id = Guid.NewGuid(),
                MasterTableId = masterTableId,
                Name = dto.Name,
                DataType = dto.DataType,
                IsEnabled = dto.IsEnabled
            };

            // Create field metadata
            var created = await _fieldsRepository.CreateAsync(field);

            // Add column to physical table
            await AddColumnToTableAsync(masterTable.TableName, field);

            return ApiResponse<MasterTableFieldDto>.SuccessWithStatus(201, MapFieldToDto(created), "Field created successfully");
        }

        public async Task<ApiResponse<MasterTableFieldDto>> UpdateFieldAsync(Guid masterTableId, Guid fieldId, UpdateMasterTableFieldDto dto)
        {
            var field = await _fieldsRepository.GetByIdAsync(fieldId);
            if (field == null || field.MasterTableId != masterTableId)
            {
                return ApiResponse<MasterTableFieldDto>.Fail(404, FieldNotFound);
            }

            if (dto.Name != null)
                field.Name = dto.Name;

            if (dto.DataType.HasValue)
                field.DataType = dto.DataType.Value;

            if (dto.IsEnabled.HasValue)
                field.IsEnabled = dto.IsEnabled.Value;

            var updated = await _fieldsRepository.UpdateAsync(field);
            return ApiResponse<MasterTableFieldDto>.Success(MapFieldToDto(updated), "Field updated successfully");
        }

        public async Task<ApiResponse<object>> DeleteFieldAsync(Guid masterTableId, Guid fieldId)
        {
            var field = await _fieldsRepository.GetByIdAsync(fieldId);
            if (field == null || field.MasterTableId != masterTableId)
            {
                return ApiResponse<object>.Fail(404, FieldNotFound);
            }

            var masterTable = await _masterTableRepository.GetByIdAsync(masterTableId);
            if (masterTable == null)
            {
                return ApiResponse<object>.Fail(404, MasterTableNotFound);
            }

            // Soft delete field metadata
            var deleted = await _fieldsRepository.DeleteAsync(fieldId);

            // Drop column from physical table
            if (deleted)
            {
                await DropColumnFromTableAsync(masterTable.TableName, field.Name);
            }

            return ApiResponse<object>.Success(null, "Field deleted successfully");
        }

        // Mapping methods
        private MasterTableDto MapToDto(MasterTable masterTable)
        {
            return new MasterTableDto
            {
                Id = masterTable.Id,
                Name = masterTable.Name,
                TableName = masterTable.TableName,
                Description = masterTable.Description,
                IsActive = masterTable.IsActive,
                CreatedAt = masterTable.CreatedAt,
                UpdatedAt = masterTable.UpdatedAt,
                Fields = masterTable.Fields.Select(MapFieldToDto).ToList()
            };
        }

        private MasterTableFieldDto MapFieldToDto(MasterTableFields field)
        {
            return new MasterTableFieldDto
            {
                Id = field.Id,
                Name = field.Name,
                DataType = field.DataType,
                IsEnabled = field.IsEnabled,
                CreatedAt = field.CreatedAt,
                UpdatedAt = field.UpdatedAt
            };
        }

        // Database table creation methods
        private async Task CreatePhysicalTableAsync(string tableName, IEnumerable<MasterTableFields> fields)
        {
            var columnDefinitions = new List<string>();

            // Add Id column (primary key)
            columnDefinitions.Add("Id CHAR(36) PRIMARY KEY");

            // Add fields
            foreach (var field in fields)
            {
                var columnType = MapDataTypeToMySql(field.DataType);
                columnDefinitions.Add($"`{field.Name}` {columnType} NULL"); // All fields nullable by default
            }

            // Add audit columns
            columnDefinitions.Add("CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
            columnDefinitions.Add("UpdatedAt DATETIME(6) NULL");
            columnDefinitions.Add("DeletedAt DATETIME(6) NULL");

            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS `{tableName}` (
                    {string.Join(",\n                    ", columnDefinitions)}
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task AddColumnToTableAsync(string tableName, MasterTableFields field)
        {
            var columnType = MapDataTypeToMySql(field.DataType);
            var alterTableSql = $@"
                ALTER TABLE `{tableName}` 
                ADD COLUMN `{field.Name}` {columnType} NULL;";

            await _dbContext.Database.ExecuteSqlRawAsync(alterTableSql);
        }

        private async Task DropColumnFromTableAsync(string tableName, string fieldName)
        {
            var alterTableSql = $@"
                ALTER TABLE `{tableName}` 
                DROP COLUMN `{fieldName}`;";

            await _dbContext.Database.ExecuteSqlRawAsync(alterTableSql);
        }

        private static string MapDataTypeToMySql(DataTypeTable dataType)
        {
            return dataType switch
            {
                DataTypeTable.STRING => "VARCHAR(255)",
                DataTypeTable.INTEGER => "INT",
                DataTypeTable.FLOAT => "DOUBLE",
                DataTypeTable.BOOLEAN => "TINYINT(1)",
                DataTypeTable.DATETIME => "DATETIME(6)",
                _ => "VARCHAR(255)"
            };
        }
    }
}
