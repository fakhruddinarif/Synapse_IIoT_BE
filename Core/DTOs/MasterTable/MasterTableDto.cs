using Core.Enums;

namespace Core.DTOs.MasterTable
{
    public class MasterTableDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<MasterTableFieldDto> Fields { get; set; } = new();
    }

    public class MasterTableFieldDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DataTypeTable DataType { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateMasterTableDto
    {
        public string Name { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = false;
        public List<CreateMasterTableFieldDto> Fields { get; set; } = new();
    }

    public class CreateMasterTableFieldDto
    {
        public string Name { get; set; } = string.Empty;
        public DataTypeTable DataType { get; set; }
        public bool IsEnabled { get; set; } = false;
    }

    public class UpdateMasterTableDto
    {
        public string? Name { get; set; }
        public string? TableName { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UpdateMasterTableFieldDto
    {
        public string? Name { get; set; }
        public DataTypeTable? DataType { get; set; }
        public bool? IsEnabled { get; set; }
    }
}
