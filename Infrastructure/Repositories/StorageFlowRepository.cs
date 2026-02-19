using Core.Entities;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class StorageFlowRepository : IStorageFlowRepository
    {
        private readonly AppDbContext _context;

        public StorageFlowRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<StorageFlow?> GetByIdAsync(Guid id, bool includeRelations = false)
        {
            var query = _context.StorageFlows
                .Where(sf => sf.DeletedAt == null && sf.Id == id);

            if (includeRelations)
            {
                query = query
                    .Include(sf => sf.MasterTable)
                        .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null))
                    .Include(sf => sf.StorageFlowDevices)
                        .ThenInclude(sfd => sfd.Device)
                    .Include(sf => sf.StorageFlowMappings)
                        .ThenInclude(sfm => sfm.MasterTableField)
                    .Include(sf => sf.StorageFlowMappings)
                        .ThenInclude(sfm => sfm.Tag);
            }

            return await query.FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<StorageFlow>> GetAllAsync(bool includeDeleted = false, bool includeRelations = false)
        {
            var query = _context.StorageFlows.AsQueryable();

            if (!includeDeleted)
            {
                query = query.Where(sf => sf.DeletedAt == null);
            }

            if (includeRelations)
            {
                query = query
                    .Include(sf => sf.MasterTable)
                        .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null))
                    .Include(sf => sf.StorageFlowDevices)
                        .ThenInclude(sfd => sfd.Device)
                    .Include(sf => sf.StorageFlowMappings)
                        .ThenInclude(sfm => sfm.MasterTableField)
                    .Include(sf => sf.StorageFlowMappings)
                        .ThenInclude(sfm => sfm.Tag);
            }

            return await query.OrderByDescending(sf => sf.CreatedAt).ToListAsync();
        }

        public async Task<IEnumerable<StorageFlow>> GetActiveFlowsAsync()
        {
            return await _context.StorageFlows
                .Where(sf => sf.IsActive && sf.DeletedAt == null)
                .Include(sf => sf.MasterTable)
                    .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null && f.IsEnabled))
                .Include(sf => sf.StorageFlowDevices)
                    .ThenInclude(sfd => sfd.Device)
                .Include(sf => sf.StorageFlowMappings)
                    .ThenInclude(sfm => sfm.MasterTableField)
                .Include(sf => sf.StorageFlowMappings)
                    .ThenInclude(sfm => sfm.Tag)
                        .ThenInclude(t => t!.Device)
                .ToListAsync();
        }

        public async Task<StorageFlow> CreateAsync(StorageFlow storageFlow)
        {
            storageFlow.Id = Guid.NewGuid();
            storageFlow.CreatedAt = DateTime.UtcNow;
            storageFlow.UpdatedAt = null;
            storageFlow.DeletedAt = null;

            await _context.StorageFlows.AddAsync(storageFlow);
            await _context.SaveChangesAsync();

            return storageFlow;
        }

        public async Task<StorageFlow> UpdateAsync(StorageFlow storageFlow)
        {
            storageFlow.UpdatedAt = DateTime.UtcNow;
            _context.StorageFlows.Update(storageFlow);
            await _context.SaveChangesAsync();

            return storageFlow;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var storageFlow = await GetByIdAsync(id);
            if (storageFlow == null) return false;

            // Soft delete
            storageFlow.DeletedAt = DateTime.UtcNow;
            storageFlow.UpdatedAt = DateTime.UtcNow;
            storageFlow.IsActive = false;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.StorageFlows
                .AnyAsync(sf => sf.Id == id && sf.DeletedAt == null);
        }

        public async Task<bool> IsTableNameInUseAsync(Guid masterTableId, Guid? excludeFlowId = null)
        {
            var query = _context.StorageFlows
                .Where(sf => sf.MasterTableId == masterTableId && sf.DeletedAt == null);

            if (excludeFlowId.HasValue)
            {
                query = query.Where(sf => sf.Id != excludeFlowId.Value);
            }

            return await query.AnyAsync();
        }
    }
}
