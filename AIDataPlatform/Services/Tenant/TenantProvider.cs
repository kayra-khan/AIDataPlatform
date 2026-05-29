namespace AIDataPlatform.Services.Tenant
{
    public class TenantProvider
    {
        private string? tenantId;

        // Is not async because it adds complexity because cosmosdbcontext global query filter cannot do async in the lambda expression.
        public string GetTenantId() => tenantId;

        public async Task SetTenantIdAsync(string id) => tenantId = id;
    }
}