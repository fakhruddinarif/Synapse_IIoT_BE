using Core.DTOs.File;

namespace Core.Interface
{
    public interface IFileService
    {
        /// <summary>
        /// Upload a single file with validation
        /// </summary>
        Task<FileUploadResponseDto> UploadSingleFileAsync(Stream fileStream, string fileName, string contentType, long fileSize, string? subDirectory = null);

        /// <summary>
        /// Upload file for specific entity field (e.g., device logo, user avatar)
        /// </summary>
        Task<FileUploadResponseDto> UploadFieldFileAsync(Stream fileStream, string fileName, string contentType, long fileSize, string entityType, Guid entityId, string fieldName);

        /// <summary>
        /// Delete a file from storage
        /// </summary>
        Task<bool> DeleteFileAsync(string filePath);

        /// <summary>
        /// Validate file extension and size
        /// </summary>
        FileValidationResult ValidateFile(string fileName, long fileSize);

        /// <summary>
        /// Get file URL from file path
        /// </summary>
        string GetFileUrl(string filePath);

        /// <summary>
        /// Get all allowed file extensions
        /// </summary>
        List<string> GetAllowedExtensions();

        /// <summary>
        /// Get maximum file size in bytes
        /// </summary>
        long GetMaxFileSize();
    }
}
