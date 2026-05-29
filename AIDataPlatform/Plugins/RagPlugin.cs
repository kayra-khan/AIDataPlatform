using System.ComponentModel;
using System.Text;
using AIDataPlatform.Services.AI;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;

namespace AIDataPlatform.Plugins
{
    /// <summary>
    /// A Semantic Kernel plugin that provides memory capabilities using Microsoft Kernel Memory
    /// </summary>
    public class RagPlugin
    {
        private readonly KernelMemoryService _memoryService;

        /// <summary>
        /// Initializes a new instance of the KernelMemoryPlugin class
        /// </summary>
        /// <param name="memoryService">The Kernel Memory service to use</param>
        public RagPlugin(KernelMemoryService memoryService)
        {
            _memoryService = memoryService;
        }

        /// <summary>
        /// Searches the memory for relevant information
        /// </summary>
        /// <param name="query">The natural language query to search for</param>
        /// <param name="index">Optional index to search in</param>
        /// <param name="minRelevance">Optional minimum relevance score (0-1)</param>
        /// <param name="limit">Optional maximum number of results</param>
        /// <returns>The search results</returns>
        [KernelFunction("search")]
        [Description("Search the memory for relevant information based on a natural language query")]
        public async Task<string> SearchAsync(
            [Description("The natural language query to search for")]
            string query,
            [Description("Optional index/collection to search in")]
            string? index = "default",
            [Description("Optional minimum relevance score (0-1)")]
            float? minRelevance = null,
            [Description("Optional maximum number of results")]
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var memory = _memoryService.KernelMemory;
                var results = await memory.SearchAsync(
                    query: query,
                    index: index,
                    //minRelevance: 0.7f,
                    cancellationToken: cancellationToken);

                // Format the results into a useful response
                StringBuilder response = new ();
                if (results.NoResult)
                {
                    return "No matching information found in memory.";
                }

                response.AppendLine($"Found {results.Results.Count} relevant results:");
                response.AppendLine();

                int i = 1;
                foreach (var result in results.Results)
                {
                    response.AppendLine($"Result {i}:");
                    //response.AppendLine($"- Relevance: {result.}");
                    response.AppendLine($"- Source: {result.SourceName}");
                    response.AppendLine(string.Join(" ", result.Partitions.Select(p => p.Text)));
                    //response.AppendLine($"- Partition: {result.PartitionText}");
                    response.AppendLine();
                    i++;
                }

                return response.ToString();
            }
            catch (Exception ex)
            {
                return $"I apologize, but I encountered an error while searching memory: {ex.Message}";
            }
        }
    }
}