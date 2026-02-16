namespace Core.Interface
{
	public interface ITokenService
	{
		string GenerateJwtToken(Guid userId, string username, string role);
		(bool IsValid, Guid UserId) ValidateJwtToken(string token);
	}
}
