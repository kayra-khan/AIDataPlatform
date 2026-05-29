using AIDataPlatform.Models;
using AIDataPlatform.Services.Tenant;
using AIDataPlatform.Services.Database;
using Microsoft.EntityFrameworkCore;
using static AIDataPlatform.Models.DataModel;

namespace AIDataPlatform.Data
{
    public class CosmosDbContext : DbContext
    {
        private string tenantId;

        public CosmosDbContext(
            DbContextOptions<CosmosDbContext> options, 
            TenantProvider tenantProvider) 
            : base(options)
        {
            this.tenantId = tenantProvider.GetTenantId();
        }

        // Required for the CosmosDbService class
        public DbSet<DataModel.Document> Documents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure the composite partition key
            modelBuilder.Entity<DataModel.Document>()
                .ToContainer("GlobalStore")
                .HasPartitionKey(e => new { e.TenantId, e.DocumentTypeId, e.DateId });

            // Apply global query filter for multi-tenancy
            modelBuilder.Entity<DataModel.Document>().HasQueryFilter(d => d.TenantId == tenantId);

            base.OnModelCreating(modelBuilder);
        }
    }
}