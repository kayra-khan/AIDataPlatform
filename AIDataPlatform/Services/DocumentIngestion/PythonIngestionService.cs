using System.Net.Http.Headers;
using Syncfusion.Blazor.Inputs;

namespace AIDataPlatform.Services.DocumentIngestion
{
    public class PythonIngestionService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PythonIngestionService> _logger;

        public PythonIngestionService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<PythonIngestionService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> UploadDocumentAsync(string tenantId, UploadFiles documentFile, string? metadata = null)
        {
            var pythonApiUrl = _configuration["DocumentIngestion:PythonApiUrl"];
            
            if (string.IsNullOrEmpty(pythonApiUrl))
            {
                throw new InvalidOperationException("Python API URL is not configured");
            }

            try
            {
                using var content = new MultipartFormDataContent();

                // Add file
                var fileStream = documentFile.File.OpenReadStream(maxAllowedSize: 500_000_000); // 500MB max
                var streamContent = new StreamContent(fileStream);
                // Don't set ContentType - let it be handled automatically or leave as default
                content.Add(streamContent, "file", documentFile.FileInfo.Name);

                // Add tenant_id
                content.Add(new StringContent(tenantId), "tenant_id");

                // Add metadata if provided
                if (!string.IsNullOrEmpty(metadata))
                {
                    content.Add(new StringContent(metadata), "metadata");
                }

                // Send request to Python API
                var response = await _httpClient.PostAsync($"{pythonApiUrl}/api/ingestion/upload", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Successfully uploaded file {documentFile.FileInfo.Name} to Python API. Response: {responseContent}");
                    return responseContent;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to upload file {documentFile.FileInfo.Name} to Python API. Status: {response.StatusCode}, Error: {errorContent}");
                    throw new HttpRequestException($"Python API returned status code {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file {documentFile.FileInfo.Name} to Python API");
                throw;
            }
        }
    }
}
