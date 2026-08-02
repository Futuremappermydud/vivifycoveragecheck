using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class CheckedMapsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<Dictionary<string, MapCheckState>> LoadAsync(string stateFilePath)
    {
        if (!File.Exists(stateFilePath))
        {
            return new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(stateFilePath);
        using var document = await JsonDocument.ParseAsync(stream);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);
        }

        var data = new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var hash = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    data[property.Name] = new MapCheckState(hash, null);
                }

                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var hashValue = property.Value.GetStringFromAny("hash", "Hash");
            if (string.IsNullOrWhiteSpace(hashValue))
            {
                continue;
            }

            var hasBundle = property.Value.GetBooleanFromAny("hasBundle", "HasBundle");
            data[property.Name] = new MapCheckState(hashValue, hasBundle);
        }

        return data;
    }

    public async Task SaveAsync(string stateFilePath, Dictionary<string, MapCheckState> checkedMaps)
    {
        await using var stream = File.Create(stateFilePath);
        await JsonSerializer.SerializeAsync(stream, checkedMaps, SerializerOptions);
    }
}
