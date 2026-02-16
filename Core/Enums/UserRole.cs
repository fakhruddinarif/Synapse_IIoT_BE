namespace Core.Enums
{
	public enum UserRole
	{
		ADMIN, // Admin has all permissions, including managing users and system settings.
		ENGINEER, // Engineer can access and modify technical data, but cannot manage users or system settings.
		OPERATOR, // Operator can view data and perform basic operations, but cannot modify technical data or manage users.
		VIEWER // Viewer can only view data and reports, but cannot perform any operations or modifications.
	}
}
