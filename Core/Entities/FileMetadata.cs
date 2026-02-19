namespace Core.Entities
{
    /// <summary>
    /// Entity to store file metadata in database
    /// </summary>
    public class FileMetadata
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FileName { get; set; } = string.Empty; // Unique file name on server
        public string OriginalFileName { get; set; } = string.Empty; // User's original file name
        public string FilePath { get; set; } = string.Empty; // Relative path from upload root
        public long FileSize { get; set; } // Size in bytes
        public string ContentType { get; set; } = string.Empty; // MIME type
        public string EntityType { get; set; } = string.Empty; // "Device", "MasterTable", "User", etc.
        public Guid? EntityId { get; set; } // ID of the related entity
        public string FieldName { get; set; } = string.Empty; // Field name (e.g., "logo", "avatar")
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeletedAt { get; set; } // Soft delete support
    }
}
