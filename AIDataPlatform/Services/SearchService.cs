using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;

namespace AIDataPlatform.Services.Search
{
    public class SearchService
    {
        private readonly SearchClient _searchClient;
        private readonly SearchIndexerClient _searchIndexerClient;

        public SearchService(IConfiguration configuration)
        {
            var searchServiceSettings = configuration.GetSection("AzureSearch");

            var credential = new AzureKeyCredential(searchServiceSettings.GetValue<string>("QueryKey"));

            _searchClient = new SearchClient(searchServiceSettings.GetValue<Uri>("EndpointUri"), searchServiceSettings.GetValue<string>("IndexName"), credential);
            _searchIndexerClient = new SearchIndexerClient(searchServiceSettings.GetValue<Uri>("EndpointUri"), credential);
        }

        public async Task<SearchResults<SearchDocument>> SearchAsync(string searchText)
        {
            var options = new SearchOptions()
            {
                IncludeTotalCount = true,
                Size = 10,
            };

            //options.Select.Add("HotelName");
            //options.Select.Add("Description");

            var response = await _searchClient.SearchAsync<SearchDocument>(searchText, options);

            return response;
        }

        public async Task RunIndexerAsync(string indexerName)
        {
            await _searchIndexerClient.RunIndexerAsync(indexerName);
        }
    }
}
