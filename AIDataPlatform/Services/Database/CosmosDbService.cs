using AIDataPlatform.Data;
using AIDataPlatform.Models;
using Microsoft.EntityFrameworkCore;
using static AIDataPlatform.Models.DataModel;

namespace AIDataPlatform.Services.Database
{
    public class CosmosDbService(CosmosDbContext dbContext)
    {
        public async Task AddDocumentAsync(DataModel.Document document)
        {
            dbContext.Documents.Add(document);
            await dbContext.SaveChangesAsync();
        }

        public async Task<DataModel.Document> GetDocumentByIdAsync(string id)
        {
            return await dbContext.Documents.FindAsync(id);
        }

        public async Task<IEnumerable<DataModel.Document>> QueryDocumentsAsync(string query)
        {
            return await RelationalQueryableExtensions
                .FromSqlRaw(dbContext.Documents, query)
                .ToListAsync();
        }

        public async Task<DataModel.Document?> GetDocumentByDocumentIdAsync(string documentId)
        {
            return await dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        }

        public async Task UpdateDocumentAsync(DataModel.Document document)
        {
            dbContext.Documents.Update(document);
            await dbContext.SaveChangesAsync();
        }

        public async Task DeleteDocumentAsync(DataModel.Document document)
        {
            dbContext.Documents.Remove(document);
            await dbContext.SaveChangesAsync();
        }

        public async Task SoftDeleteDocumentAsync(DataModel.Document document)
        {
            document.IsDeleted = true;
            await UpdateDocumentAsync(document);
        }
    }
}

/* Cosmosdb without ef core

using Microsoft.Azure.Cosmos;
using static AIDataPlatform.Helpers.DataModel;

namespace AIDataPlatform.Services.Database
{
    public class CosmosDbService
    {
        private readonly Container container;

        public CosmosDbService(IConfiguration configuration)
        {
            var cosmosDbSettings = configuration.GetSection("AzureCosmosDb");

            var cosmosClient = new CosmosClient(cosmosDbSettings.GetValue<string>("EndpointUri"), cosmosDbSettings.GetValue<string>("PrimaryKey"), new CosmosClientOptions());

            container = cosmosClient.GetContainer(cosmosDbSettings.GetValue<string>("DatabaseName"), cosmosDbSettings.GetValue<string>("ContainerName"));
        }

        public async Task AddDocumentAsync<T>(Document document) where T : class
        {
            var partitionKey = new PartitionKeyBuilder()
                .Add(document.TenantId)
                .Add(document.DocumentTypeId)
                .Add(document.DateId)
                .Build();

            await container.CreateItemAsync(document, partitionKey);
        }

        public async Task<T> GetDocumentByIdAsync<T>(string id, string tenantId, string documentTypeId, string dateId) where T : class
        {
            try
            {
                var partitionKey = new PartitionKeyBuilder()
                    .Add(tenantId)
                    .Add(documentTypeId)
                    .Add(dateId)
                    .Build();

                var response = await container.ReadItemAsync<T>(id, partitionKey);
                return response.Resource;
            }
            catch (CosmosException e)
            {
                throw new Exception("An error occurred while retrieving the document.", e);
            }
        }

        public async Task<IEnumerable<T>> QueryDocumentsAsync<T>(string query, string tenantId = default, string documentTypeId = default, string dateId = default) where T : class
        {
            try
            {
                PartitionKey? partitionKey = default;
                if (tenantId != default && documentTypeId != default && dateId != default)
                {
                    partitionKey = new PartitionKeyBuilder()
                        .Add(tenantId)
                        .Add(documentTypeId)
                        .Add(dateId)
                        .Build();
                }

                var queryDefinition = new QueryDefinition(query);
                var queryOptions = new QueryRequestOptions();

                if (partitionKey.HasValue)
                {
                    queryOptions.PartitionKey = partitionKey.Value;
                }

                var iterator = container.GetItemQueryIterator<T>(queryDefinition, requestOptions: queryOptions);
                
                var items = new List<T>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    items.AddRange(response.ToList());
                }
                return items;
            }
            catch (CosmosException e)
            {
                throw new Exception("An error occurred while querying the documents.", e);
            }
        }

        // Query document by documentId helper method
        public async Task<Document?> GetDocumentByDocumentIdAsync(string documentId)
        {
            var query = $"SELECT * FROM GlobalStore WHERE GlobalStore.DocumentId = '{documentId}'";
            var documents = await QueryDocumentsAsync<Document>(query);
            return documents.FirstOrDefault();
        }


        public async Task UpdateDocumentAsync<T>(Document document) where T : class
        {
            var partitionKey = new PartitionKeyBuilder()
                    .Add(document.TenantId)
                    .Add(document.DocumentTypeId)
                    .Add(document.DateId)
                    .Build();

            await container.ReplaceItemAsync(document, document.id, partitionKey);
        }

        public async Task DeleteDocumentAsync<T>(Document document) where T : class
        {
            var partitionKey = new PartitionKeyBuilder()
                    .Add(document.TenantId)
                    .Add(document.DocumentTypeId)
                    .Add(document.DateId)
                    .Build();

            await container.DeleteItemAsync<T>(document.id, partitionKey);
        }

        public async Task SoftDeleteDocumentAsync<T>(Document document) where T : class
        {
            await UpdateDocumentAsync<Document>(document);
        }
    }
}
*/