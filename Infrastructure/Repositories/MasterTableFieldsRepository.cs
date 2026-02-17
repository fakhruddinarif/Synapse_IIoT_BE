using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class MasterTableFieldsRepository : IMasterTableFieldsRepository
    {
        private readonly AppDbContext _context;

        public MasterTableFieldsRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MasterTableFields>> GetByMasterTableIdAsync(Guid masterTableId)
        {
            return await _context.MasterTableFields
                .Where(f => f.MasterTableId == masterTableId && f.DeletedAt == null)
                .OrderBy(f => f.CreatedAt)
                .ToListAsync();
        }

        public async Task<MasterTableFields?> GetByIdAsync(Guid id)
        {
            return await _context.MasterTableFields
                .Where(f => f.Id == id && f.DeletedAt == null)
                .FirstOrDefaultAsync();
        }

        public async Task<MasterTableFields> CreateAsync(MasterTableFields field)
        {
            field.CreatedAt = DateTime.UtcNow;
            _context.MasterTableFields.Add(field);
            await _context.SaveChangesAsync();
            return field;
        }

        public async Task<MasterTableFields> UpdateAsync(MasterTableFields field)
        {
            field.UpdatedAt = DateTime.UtcNow;
            _context.MasterTableFields.Update(field);
            await _context.SaveChangesAsync();
            return field;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var field = await _context.MasterTableFields.FindAsync(id);
            if (field == null || field.DeletedAt != null)
                return false;

            // Soft delete
            field.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.MasterTableFields
                .AnyAsync(f => f.Id == id && f.DeletedAt == null);
        }
    }
}
