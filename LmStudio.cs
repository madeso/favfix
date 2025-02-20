using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace favfix;

internal class LmStudio(string host, int port)
{
    private HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri($"http://{host}:{port}");
        return client;
    }

    public async Task<ImmutableArray<string>> GetModels()
    {
        using var client = CreateClient();
        var json = await client.GetStringAsync("v1/models");
        var ret = JsonSerializer.Deserialize<ModelList>(json);
        if (ret == null) throw new Exception("Failed to call lm studio");
        return ret.Models.Select(x => x.Id).ToImmutableArray();
    }

    public async Task<CompletionResponse> GetCompletions(string model, string message)
    {
        using var client = CreateClient();
        var inputBlob = new CompletionRequest()
        {
            Model = model,
            Messages =
            [
                new Message()
                {
                    Role = "user",
                    Content = message
                }
            ]
        };
        var inputContent = new StringContent(JsonSerializer.Serialize(inputBlob), Encoding.UTF8, "application/json");
        var responseObject = await client.PostAsync("v1/chat/completions", inputContent);
        var responseJson = await responseObject.Content.ReadAsStringAsync();

        var response = JsonSerializer.Deserialize<CompletionResponse>(responseJson);
        if (response == null) throw new Exception("Failed to call lm studio");
        return response;
    }
}

internal class SingleModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
}

internal class ModelList
{
    [JsonPropertyName("data")] public ImmutableArray<SingleModel> Models { get; set; } = [];
}

internal class Message
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

internal class CompletionRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<Message> Messages { get; set; } = [];
}

internal class CompletionMessage
{
    [JsonPropertyName("role")] public string Role {get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}
internal class CompletionChoice
{
    // add logprobs?
    [JsonPropertyName("index")] public int Index {get; set; }
    [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = "";
    [JsonPropertyName("message")] public CompletionMessage Message { get; set; } = new();
}
internal class CompletionUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens {get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens {get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}
internal class CompletionResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("object")] public string Object {get; set;}
    [JsonPropertyName("created")] public int Created {get; set;}
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("choices")] public ImmutableArray<CompletionChoice> Choices { get; set; } = [];
    [JsonPropertyName("usage")] public CompletionUsage Usage { get; set; } = new();
    [JsonPropertyName("system_fingerprint")] public string SystemFingerprint { get; set; } = "";

}