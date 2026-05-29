using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AIDataPlatform.Services.AI;

public class AutoGenMessage
{
    public string source { get; set; } = string.Empty;
    public object? models_usage { get; set; }
    public Dictionary<string, object>? metadata { get; set; }
    public string content { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
}

public interface IAutoGenStreamingService
{
    IAsyncEnumerable<string> StreamChatAsync(string message, string model, string containerUrl, 
        CancellationToken cancellationToken = default);
}

public class AutoGenStreamingService(HttpClient httpClient) : IAutoGenStreamingService
{
    public async IAsyncEnumerable<string> StreamChatAsync(string message, string model, string containerUrl, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For localhost testing
        if (containerUrl?.Contains("localhost") != true && string.IsNullOrEmpty(containerUrl))
        {
            containerUrl = "http://localhost:8000";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{containerUrl}/api/chat/autogen");
        
        var requestBody = new
        {
            message = message,
            model = model
        };

        var json = JsonSerializer.Serialize(requestBody);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Accept", "text/event-stream");

        HttpResponseMessage? response = null;
        
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var data = default(AutoGenMessage);
                    
                    try
                    {
                       data = JsonSerializer.Deserialize<AutoGenMessage>(line);
                    }
                    catch
                    {
                        data = null;
                    }
                    
                    if (data != null && !string.IsNullOrEmpty(data.content))
                    {
                        yield return data.content;
                    }

                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }
}