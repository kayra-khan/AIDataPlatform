using AIDataPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDataPlatform.Services.Tenant
{
    public class TenantService(ApplicationDbContext context)
    {
        public async Task<List<string?>> GetAllTenantIdsAsync()
        {
            return await context.Tenants.Select(t => t.Id).ToListAsync();
        }
    }
}