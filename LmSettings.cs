using System.Text.Json;
using System.Text.Json.Serialization;

namespace favfix;

public class LmSettings
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = -1;

    [JsonPropertyName("selected_model")]
    public string SelectedModel { get; set; } = "";

    public static LmSettings LoadFile(string path)
    {
        if (File.Exists(path) == false) return new LmSettings();
        
        var content = File.ReadAllText(path);
        var body = JsonSerializer.Deserialize<LmSettings>(content);
        if(body == null) return new LmSettings();

        return body;
    }

    private static readonly JsonSerializerOptions PrettyPrint = new()
    {
        WriteIndented = true
    };

    public void Save(string path)
    {
        var body = JsonSerializer.Serialize(this, PrettyPrint);
        File.WriteAllText(path, body);
    }
}