using Azure;
using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.FormRecognizer.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace AIDataPlatform.Services.DocumentIntelligence
{
    public class DocumentIntelligenceService
    {
        private readonly DocumentAnalysisClient _documentAnalysisClient;

        public DocumentIntelligenceService(IConfiguration configuration)
        {
            string? apiKey = configuration["DocumentIntelligence:ApiKey"];
            string? endpoint = configuration["DocumentIntelligence:Endpoint"];

            _documentAnalysisClient = new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        }

        public async Task<AnalyzeResult> ExtractDataFromUriAsync(Uri documentUri)
        {
            AnalyzeDocumentOperation? operation = null;
            AnalyzeResult? result = null;

            try
            {
                operation = await _documentAnalysisClient.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-document", documentUri);

                if (operation.HasValue)
                {
                    result = await operation.WaitForCompletionAsync();
                }
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine(ex);
            }
            return result;
        }

		public async Task<AnalyzeResult> ExtractDataFromStreamAsync(Stream inputStream)
		{
			AnalyzeDocumentOperation? operation = null;
			AnalyzeResult? result = null;

			try
			{
                operation = await _documentAnalysisClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-document", inputStream);

				if (operation.HasValue)
				{
					result = await operation.WaitForCompletionAsync();
				}
			}
			catch (RequestFailedException ex)
			{
				Console.WriteLine(ex);
			}
			return result;
		}
	}
}
