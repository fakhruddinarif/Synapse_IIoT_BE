using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
	public class UserRepository : IUserRepository
	{
		private readonly AppDbContext _context;

		public UserRepository(AppDbContext context)
		{
			_context = context;
		}

		public async Task<User?> GetByIdAsync(Guid id)
		{
			return await _context.Users
				.Where(u => u.DeletedAt == null)
				.FirstOrDefaultAsync(u => u.Id == id);
		}

		public async Task<User?> GetByUsernameAsync(string username)
		{
			return await _context.Users
				.Where(u => u.DeletedAt == null)
				.FirstOrDefaultAsync(u => u.Username == username);
		}

		public async Task<User> CreateAsync(User user)
		{
			_context.Users.Add(user);
			await _context.SaveChangesAsync();
			return user;
		}

		public async Task<User> UpdateAsync(User user)
		{
			user.UpdatedAt = DateTime.UtcNow;
			_context.Users.Update(user);
			await _context.SaveChangesAsync();
			return user;
		}

		public async Task<bool> UsernameExistsAsync(string username)
		{
			return await _context.Users
				.Where(u => u.DeletedAt == null)
				.AnyAsync(u => u.Username == username);
		}
	}
}
