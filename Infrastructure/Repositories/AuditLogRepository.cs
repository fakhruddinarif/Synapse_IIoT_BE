using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories
{
	/// <summary>
	/// Repository implementation for audit log operations
	/// Provides data access layer for compliance and security tracking
	/// </summary>
	public class AuditLogRepository : IAuditLogRepository
	{
		private readonly AppDbContext _context;
		private readonly ILogger<AuditLogRepository> _logger;

		public AuditLogRepository(AppDbContext context, ILogger<AuditLogRepository> logger)
		{
			_context = context;
			_logger = logger;
		}

		/// <summary>
		/// Create and persist a new audit log entry to the database
		/// </summary>
		public async Task<AuditLog> CreateAsync(AuditLog auditLog)
		{
			try
			{
				_context.AuditLogs.Add(auditLog);
				await _context.SaveChangesAsync();
				_logger.LogInformation("Audit log created: {Action} on {EntityType}/{EntityId}", auditLog.Action, auditLog.EntityType, auditLog.EntityId);
				return auditLog;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error creating audit log: {Exception}", ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Retrieve audit logs with optional filtering by action, resource type, user, or date range
		/// </summary>
		public async Task<IEnumerable<AuditLog>> GetAsync(string? action = null, string? resourceType = null, Guid? userId = null, DateTime? fromDate = null, int skip = 0, int take = 50)
		{
			try
			{
				var query = _context.AuditLogs.AsQueryable();

				// Apply filters
				if (!string.IsNullOrEmpty(action))
					query = query.Where(a => a.Action == action);

				if (!string.IsNullOrEmpty(resourceType))
					query = query.Where(a => a.EntityType == resourceType);

				if (userId.HasValue)
					query = query.Where(a => a.UserId == userId);

				if (fromDate.HasValue)
					query = query.Where(a => a.CreatedAt >= fromDate.Value);

				// Order by newest first and apply pagination
				query = query.OrderByDescending(a => a.CreatedAt);

				return await query
					.Skip(skip)
					.Take(take)
					.ToListAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving audit logs: {Exception}", ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Get total count of audit logs matching the filter criteria, used for pagination calculations
		/// </summary>
		public async Task<int> GetTotalCountAsync(string? action = null, string? resourceType = null, Guid? userId = null, DateTime? fromDate = null)
		{
			try
			{
				var query = _context.AuditLogs.AsQueryable();

				if (!string.IsNullOrEmpty(action))
					query = query.Where(a => a.Action == action);

				if (!string.IsNullOrEmpty(resourceType))
					query = query.Where(a => a.EntityType == resourceType);

				if (userId.HasValue)
					query = query.Where(a => a.UserId == userId);

				if (fromDate.HasValue)
					query = query.Where(a => a.CreatedAt >= fromDate.Value);

				return await query.CountAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error getting audit log count: {Exception}", ex.Message);
				throw;
			}
		}

	/// <summary>
	/// Retrieve all audit log entries for a specific user, ordered by most recent first
	/// </summary>
	public async Task<IEnumerable<AuditLog>> GetByUserIdAsync(Guid userId, int skip = 0, int take = 50)
	{
		try
		{
			return await _context.AuditLogs
				.Where(a => a.UserId == userId)
				.OrderByDescending(a => a.CreatedAt)
				.Skip(skip)
				.Take(take)
				.ToListAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving audit logs for user {UserId}", userId);
			throw;
		}
	}

	/// <summary>
	/// Retrieve all changes made to a specific resource (e.g., all modifications to a device)
	/// </summary>
	public async Task<IEnumerable<AuditLog>> GetByResourceAsync(string resourceType, Guid resourceId, int skip = 0, int take = 50)
	{
		try
		{
			return await _context.AuditLogs
				.Where(a => a.EntityType == resourceType && a.EntityId == resourceId)
				.OrderByDescending(a => a.CreatedAt)
				.Skip(skip)
				.Take(take)
				.ToListAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving audit logs for {ResourceType}/{ResourceId}", resourceType, resourceId);
			throw;
		}
	}
    }
}
