using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Entities;

namespace Core.Interface
{
	/// <summary>
	/// Repository interface for audit log operations
	/// Handles creation and retrieval of audit logs for compliance and security tracking
	/// </summary>
	public interface IAuditLogRepository
	{
		/// <summary>
		/// Create a new audit log entry
		/// </summary>
		Task<AuditLog> CreateAsync(AuditLog auditLog);

		/// <summary>
		/// Get audit logs with optional filtering
		/// </summary>
		Task<IEnumerable<AuditLog>> GetAsync(string? action = null, string? resourceType = null, Guid? userId = null, DateTime? fromDate = null, int skip = 0, int take = 50);

		/// <summary>
		/// Get total count of audit logs for pagination
		/// </summary>
		Task<int> GetTotalCountAsync(string? action = null, string? resourceType = null, Guid? userId = null, DateTime? fromDate = null);

		/// <summary>
		/// Get audit logs for a specific user
		/// </summary>
		Task<IEnumerable<AuditLog>> GetByUserIdAsync(Guid userId, int skip = 0, int take = 50);

		/// <summary>
		/// Get audit logs for a specific resource
		/// </summary>
		Task<IEnumerable<AuditLog>> GetByResourceAsync(string resourceType, Guid resourceId, int skip = 0, int take = 50);
	}
}
