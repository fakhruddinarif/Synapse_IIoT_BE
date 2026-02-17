using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class MasterTableRepository : IMasterTableRepository
    {
        private readonly AppDbContext _context;

        public MasterTableRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MasterTable>> GetAllAsync()
        {
            return await _context.MasterTables
                .Where(mt => mt.DeletedAt == null)
                .Include(mt => mt.Fields.Where(f => f.DeletedAt == null))
                .OrderByDescending(mt => mt.CreatedAt)
                .ToListAsync();
        }

        public async Task<MasterTable?> GetByIdAsync(Guid id)
        {
            return await _context.MasterTables
                .Where(mt => mt.Id == id && mt.DeletedAt == null)
                .Include(mt => mt.Fields.Where(f => f.DeletedAt == null))
                .FirstOrDefaultAsync();
        }

        public async Task<MasterTable?> GetByTableNameAsync(string tableName)
        {
            return await _context.MasterTables
                .Where(mt => mt.TableName == tableName && mt.DeletedAt == null)
                .Include(mt => mt.Fields.Where(f => f.DeletedAt == null))
                .FirstOrDefaultAsync();
        }

        public async Task<MasterTable> CreateAsync(MasterTable masterTable)
        {
            masterTable.CreatedAt = DateTime.UtcNow;
            _context.MasterTables.Add(masterTable);
            await _context.SaveChangesAsync();
            return masterTable;
        }

        public async Task<MasterTable> UpdateAsync(MasterTable masterTable)
        {
            masterTable.UpdatedAt = DateTime.UtcNow;
            _context.MasterTables.Update(masterTable);
            await _context.SaveChangesAsync();
            return masterTable;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var masterTable = await _context.MasterTables.FindAsync(id);
            if (masterTable == null || masterTable.DeletedAt != null)
                return false;

            // Soft delete
            masterTable.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.MasterTables
                .AnyAsync(mt => mt.Id == id && mt.DeletedAt == null);
        }

        public async Task<bool> TableNameExistsAsync(string tableName, Guid? excludeId = null)
        {
            var query = _context.MasterTables
                .Where(mt => mt.TableName == tableName && mt.DeletedAt == null);

            if (excludeId.HasValue)
            {
                query = query.Where(mt => mt.Id != excludeId.Value);
            }

            return await query.AnyAsync();
        }
    }
}
