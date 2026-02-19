using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Core.Interface;
using Core.DTOs;
using Core.DTOs.File;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FileController : ControllerBase
    {
        private readonly IFileService _fileService;
        private readonly ILogger<FileController> _logger;

        public FileController(IFileService fileService, ILogger<FileController> logger)
        {
            _fileService = fileService;
            _logger = logger;
        }

        /// <summary>
        /// Upload a single file
        /// </summary>
        /// <param name="file">File to upload</param>
        /// <param name="subDirectory">Optional subdirectory (e.g., "documents", "images")</param>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadSingleFile(IFormFile file, [FromQuery] string? subDirectory = null)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Status = 400,
                        Message = "No file provided"
                    });
                }

                using var stream = file.OpenReadStream();
                var result = await _fileService.UploadSingleFileAsync(
                    stream, 
                    file.FileName, 
                    file.ContentType, 
                    file.Length, 
                    subDirectory);
                
                return Ok(new ApiResponse<FileUploadResponseDto>
                {
                    Status = 200,
                    Message = "File uploaded successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file");
                return BadRequest(new ApiResponse<object>
                {
                    Status = 400,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Upload multiple files at once
        /// </summary>
        /// <param name="files">List of files to upload</param>
        /// <param name="subDirectory">Optional subdirectory</param>
        [HttpPost("upload-multiple")]
        public async Task<IActionResult> UploadMultipleFiles(List<IFormFile> files, [FromQuery] string? subDirectory = null)
        {
            try
            {
                if (files == null || files.Count == 0)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Status = 400,
                        Message = "No files provided"
                    });
                }

                var result = new MultipleFileUploadResponseDto();

                foreach (var file in files)
                {
                    try
                    {
                        using var stream = file.OpenReadStream();
                        var uploadedFile = await _fileService.UploadSingleFileAsync(
                            stream,
                            file.FileName,
                            file.ContentType,
                            file.Length,
                            subDirectory);
                        
                        result.UploadedFiles.Add(uploadedFile);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to upload file: {file.FileName}");
                        result.FailedFiles.Add($"{file.FileName}: {ex.Message}");
                        result.FailedCount++;
                    }
                }
                
                return Ok(new ApiResponse<MultipleFileUploadResponseDto>
                {
                    Status = 200,
                    Message = $"{result.SuccessCount} file(s) uploaded successfully, {result.FailedCount} failed",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading multiple files");
                return BadRequest(new ApiResponse<object>
                {
                    Status = 400,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Upload file for specific entity field (e.g., device logo, user avatar)
        /// </summary>
        /// <param name="file">File to upload</param>
        /// <param name="entityType">Entity type (e.g., "Device", "User", "MasterTable")</param>
        /// <param name="entityId">Entity ID</param>
        /// <param name="fieldName">Field name (e.g., "logo", "avatar", "attachment")</param>
        [HttpPost("upload-field")]
        public async Task<IActionResult> UploadFieldFile(
            IFormFile file,
            [FromQuery] string entityType,
            [FromQuery] Guid entityId,
            [FromQuery] string fieldName)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Status = 400,
                        Message = "No file provided"
                    });
                }

                if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(fieldName))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Status = 400,
                        Message = "EntityType and FieldName are required"
                    });
                }

                using var stream = file.OpenReadStream();
                var result = await _fileService.UploadFieldFileAsync(
                    stream,
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    entityType,
                    entityId,
                    fieldName);
                
                return Ok(new ApiResponse<FileUploadResponseDto>
                {
                    Status = 200,
                    Message = $"File uploaded for {entityType}.{fieldName}",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading field file");
                return BadRequest(new ApiResponse<object>
                {
                    Status = 400,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Delete a file by its path
        /// </summary>
        /// <param name="filePath">Relative file path from uploads root</param>
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFile([FromQuery] string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Status = 400,
                        Message = "File path is required"
                    });
                }

                var result = await _fileService.DeleteFileAsync(filePath);
                
                if (result)
                {
                    return Ok(new ApiResponse<object>
                    {
                        Status = 200,
                        Message = "File deleted successfully"
                    });
                }
                else
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Status = 404,
                        Message = "File not found"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file");
                return BadRequest(new ApiResponse<object>
                {
                    Status = 400,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get allowed file extensions and max file size
        /// </summary>
        [HttpGet("config")]
        [AllowAnonymous]
        public IActionResult GetUploadConfig()
        {
            var config = new
            {
                AllowedExtensions = _fileService.GetAllowedExtensions(),
                MaxFileSize = _fileService.GetMaxFileSize(),
                MaxFileSizeMB = Math.Round(_fileService.GetMaxFileSize() / (1024.0 * 1024.0), 2)
            };

            return Ok(new ApiResponse<object>
            {
                Status = 200,
                Message = "Upload configuration retrieved",
                Data = config
            });
        }
    }
}
