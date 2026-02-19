using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Core.Interface;
using Core.DTOs.File;
using Core.Entities;
using Core.Exceptions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services
{
    public class FileService : IFileService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FileService> _logger;
        private readonly AppDbContext _context;
        private readonly string _uploadPath;
        private readonly List<string> _allowedExtensions;
        private readonly long _maxFileSize;
        private readonly string _baseUrl;

        public FileService(
            IConfiguration configuration,
            ILogger<FileService> logger,
            AppDbContext context)
        {
            _configuration = configuration;
            _logger = logger;
            _context = context;

            // Get settings from configuration
            var uploadPathConfig = _configuration["FileUploadSettings:UploadPath"] ?? "wwwroot/uploads";
            
            // Handle both absolute and relative paths
            if (Path.IsPathRooted(uploadPathConfig))
            {
                _uploadPath = uploadPathConfig;
            }
            else
            {
                // If relative, combine with current directory
                _uploadPath = Path.Combine(Directory.GetCurrentDirectory(), uploadPathConfig);
            }
            
            _baseUrl = _configuration["FileUploadSettings:BaseUrl"] ?? "http://localhost:5000";
            
            var allowedExtensionsConfig = _configuration["FileUploadSettings:AllowedExtensions"] ?? 
                ".jpg,.jpeg,.png,.gif,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.zip";
            _allowedExtensions = allowedExtensionsConfig.Split(',').Select(e => e.Trim().ToLower()).ToList();
            
            _maxFileSize = long.TryParse(_configuration["FileUploadSettings:MaxFileSizeInBytes"], out var size) 
                ? size 
                : 10485760; // Default 10MB

            // Ensure upload directory exists
            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
                _logger.LogInformation($"Created upload directory: {_uploadPath}");
            }
        }

        public async Task<FileUploadResponseDto> UploadSingleFileAsync(Stream fileStream, string fileName, string contentType, long fileSize, string? subDirectory = null)
        {
            // Validate file
            var validationResult = ValidateFile(fileName, fileSize);
            if (!validationResult.IsValid)
            {
                throw new BadRequestException(validationResult.ErrorMessage);
            }

            // Generate unique file name
            var fileExtension = Path.GetExtension(fileName).ToLower();
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";

            // Determine upload path
            var uploadDirectory = _uploadPath;
            if (!string.IsNullOrWhiteSpace(subDirectory))
            {
                uploadDirectory = Path.Combine(_uploadPath, subDirectory);
                if (!Directory.Exists(uploadDirectory))
                {
                    Directory.CreateDirectory(uploadDirectory);
                }
            }

            var filePath = Path.Combine(uploadDirectory, uniqueFileName);

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(stream);
            }

            _logger.LogInformation($"File uploaded successfully: {uniqueFileName}");

            // Calculate relative path
            var relativePath = Path.GetRelativePath(_uploadPath, filePath).Replace("\\", "/");

            // Save metadata to database
            var metadata = new FileMetadata
            {
                FileName = uniqueFileName,
                OriginalFileName = fileName,
                FilePath = relativePath,
                FileSize = fileSize,
                ContentType = contentType,
                EntityType = "General",
                UploadedAt = DateTime.UtcNow
            };

            _context.Set<FileMetadata>().Add(metadata);
            await _context.SaveChangesAsync();

            return new FileUploadResponseDto
            {
                FileName = uniqueFileName,
                OriginalFileName = fileName,
                FilePath = relativePath,
                FileUrl = GetFileUrl(relativePath),
                FileSize = fileSize,
                ContentType = contentType,
                UploadedAt = metadata.UploadedAt
            };
        }

        public async Task<FileUploadResponseDto> UploadFieldFileAsync(Stream fileStream, string fileName, string contentType, long fileSize, string entityType, Guid entityId, string fieldName)
        {
            // Validate file
            var validationResult = ValidateFile(fileName, fileSize);
            if (!validationResult.IsValid)
            {
                throw new BadRequestException(validationResult.ErrorMessage);
            }

            // Create subdirectory for entity type
            var subDirectory = Path.Combine(entityType.ToLower(), entityId.ToString());
            var uploadDirectory = Path.Combine(_uploadPath, subDirectory);
            
            if (!Directory.Exists(uploadDirectory))
            {
                Directory.CreateDirectory(uploadDirectory);
            }

            // Check if file already exists for this field and delete it
            var existingFile = await _context.Set<FileMetadata>()
                .FirstOrDefaultAsync(f => 
                    f.EntityType == entityType && 
                    f.EntityId == entityId && 
                    f.FieldName == fieldName &&
                    f.DeletedAt == null);

            if (existingFile != null)
            {
                // Delete old file
                var oldFilePath = Path.Combine(_uploadPath, existingFile.FilePath);
                if (File.Exists(oldFilePath))
                {
                    File.Delete(oldFilePath);
                }
                
                // Soft delete metadata
                existingFile.DeletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Replaced existing file for {entityType} {entityId} - {fieldName}");
            }

            // Generate unique file name
            var fileExtension = Path.GetExtension(fileName).ToLower();
            var uniqueFileName = $"{fieldName}_{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uploadDirectory, uniqueFileName);

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(stream);
            }

            // Calculate relative path
            var relativePath = Path.GetRelativePath(_uploadPath, filePath).Replace("\\", "/");

            // Save metadata to database
            var metadata = new FileMetadata
            {
                FileName = uniqueFileName,
                OriginalFileName = fileName,
                FilePath = relativePath,
                FileSize = fileSize,
                ContentType = contentType,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                UploadedAt = DateTime.UtcNow
            };

            _context.Set<FileMetadata>().Add(metadata);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Field file uploaded: {entityType}/{entityId}/{fieldName}");

            return new FileUploadResponseDto
            {
                FileName = uniqueFileName,
                OriginalFileName = fileName,
                FilePath = relativePath,
                FileUrl = GetFileUrl(relativePath),
                FileSize = fileSize,
                ContentType = contentType,
                UploadedAt = metadata.UploadedAt
            };
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            try
            {
                var fullPath = Path.Combine(_uploadPath, filePath);
                
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation($"File deleted: {filePath}");

                    // Soft delete metadata
                    var metadata = await _context.Set<FileMetadata>()
                        .FirstOrDefaultAsync(f => f.FilePath == filePath && f.DeletedAt == null);
                    
                    if (metadata != null)
                    {
                        metadata.DeletedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete file: {filePath}");
                return false;
            }
        }

        public FileValidationResult ValidateFile(string fileName, long fileSize)
        {
            // Check if file size is zero
            if (fileSize == 0)
            {
                return new FileValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File is empty"
                };
            }

            // Check file size
            if (fileSize > _maxFileSize)
            {
                var maxSizeMB = _maxFileSize / (1024.0 * 1024.0);
                return new FileValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"File size exceeds maximum allowed size of {maxSizeMB:F2} MB"
                };
            }

            // Check file extension
            var fileExtension = Path.GetExtension(fileName).ToLower();
            if (string.IsNullOrWhiteSpace(fileExtension) || !_allowedExtensions.Contains(fileExtension))
            {
                return new FileValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}"
                };
            }

            // Additional security: Check for double extensions (e.g., file.php.jpg)
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (fileNameWithoutExtension.Contains('.'))
            {
                return new FileValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File name contains multiple extensions which is not allowed"
                };
            }

            return new FileValidationResult
            {
                IsValid = true
            };
        }

        public string GetFileUrl(string filePath)
        {
            return $"{_baseUrl}/uploads/{filePath.Replace("\\", "/")}";
        }

        public List<string> GetAllowedExtensions()
        {
            return _allowedExtensions;
        }

        public long GetMaxFileSize()
        {
            return _maxFileSize;
        }
    }
}
