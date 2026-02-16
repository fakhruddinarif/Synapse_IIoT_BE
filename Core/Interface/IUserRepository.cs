using Core.Entities;

namespace Core.Interface
{
	public interface IUserRepository
	{
		Task<User?> GetByIdAsync(Guid id);
		Task<User?> GetByUsernameAsync(string username);
		Task<User> CreateAsync(User user);
		Task<User> UpdateAsync(User user);
		Task<bool> UsernameExistsAsync(string username);
	}
}
