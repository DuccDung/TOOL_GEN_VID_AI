using System.Text.Json;
using System.Text.Json.Serialization;

namespace TOOL_LOCAL.AI;

public sealed class StructuredJsonParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public T Parse<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("AI provider returned an empty JSON response.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException("AI provider returned JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"AI response does not match contract {typeof(T).Name}.", exception);
        }
    }
}
