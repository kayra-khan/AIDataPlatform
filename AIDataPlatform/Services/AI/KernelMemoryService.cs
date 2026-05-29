using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;

namespace AIDataPlatform.Services.AI
{
    public class KernelMemoryService
    {
        public IKernelMemory KernelMemory { get; private set; }
        private int chunkSize = 100;

        public KernelMemoryService(IConfiguration configuration)
        {
            var azureAISearchSettings = configuration.GetSection("AzureSearch");

            AzureAISearchConfig azureAISearchConfig = new()
            {
                APIKey = azureAISearchSettings.GetValue<string>("ApiKey"),
                Endpoint = azureAISearchSettings.GetValue<string>("EndpointUri"),
                Auth = AzureAISearchConfig.AuthTypes.APIKey,
                UseHybridSearch = true
            };

            var openAISettings = configuration.GetSection("OpenAI");

            OpenAIConfig openAIConfig = new()
            {
                APIKey = openAISettings.GetValue<string>("ApiKey"),
                EmbeddingModel = openAISettings.GetValue<string>("EmbeddingModel"),
                TextModel = openAISettings.GetValue<string>("TextModel")
            };
            
            // Customize memory records size (in tokens)
            var textPartitioningOptions = new TextPartitioningOptions
            {
                MaxTokensPerParagraph = chunkSize,
                OverlappingTokens = 0,
            };

            var kernelMemoryBuilder = new KernelMemoryBuilder()
                .WithAzureAISearchMemoryDb(azureAISearchConfig)
                .WithOpenAITextEmbeddingGeneration(openAIConfig)
                .WithOpenAITextGeneration(openAIConfig);

            // can be configured independently if higher logging is needed for this specific service only
            //kernelMemoryBuilder.Services.AddLogging(c => c.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Information));

            KernelMemoryBuilderBuildOptions options = new()
            {
                AllowMixingVolatileAndPersistentData = true
            };

            KernelMemory = kernelMemoryBuilder.Build<MemoryServerless>(options);
        }
    }
}