using System.Text.Json;
using System.Text.Json.Serialization;

namespace WingetAgent.Output;

/// <summary>Shared JSON settings so updates.json / annotations.json round-trip identically.</summary>
public static class JsonIo
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Write(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, Options));

    public static T? Read<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
}
