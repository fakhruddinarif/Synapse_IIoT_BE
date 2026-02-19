namespace Core.DTOs.File
{
    /// <summary>
    /// Response after successful file upload
    /// </summary>
    public class FileUploadResponseDto
    {
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    /// <summary>
    /// Response for multiple file uploads
    /// </summary>
    public class MultipleFileUploadResponseDto
    {
        public List<FileUploadResponseDto> UploadedFiles { get; set; } = new();
        public List<string> FailedFiles { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
    }

    /// <summary>
    /// Request for field-based file upload (e.g., upload logo for a device)
    /// </summary>
    public class FieldFileUploadRequestDto
    {
        public string EntityType { get; set; } = string.Empty; // "Device", "MasterTable", etc.
        public Guid EntityId { get; set; }
        public string FieldName { get; set; } = string.Empty; // e.g., "logo", "avatar", "attachment"
    }

    /// <summary>
    /// File metadata DTO
    /// </summary>
    public class FileMetadataDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    /// <summary>
    /// File validation result
    /// </summary>
    public class FileValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
