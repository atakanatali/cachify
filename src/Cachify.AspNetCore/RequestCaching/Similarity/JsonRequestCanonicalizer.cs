using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Cachify.AspNetCore;

/// <summary>
/// Canonicalizes JSON and text payloads by normalizing structure and removing noise fields.
/// </summary>
public sealed class JsonRequestCanonicalizer : IRequestCanonicalizer
{
    private readonly SimilarityRequestCacheOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRequestCanonicalizer"/> class.
    /// </summary>
    /// <param name="options">The request cache options.</param>
    public JsonRequestCanonicalizer(IOptions<RequestCacheOptions> options)
    {
        _options = options.Value.Similarity;
    }

    /// <inheritdoc />
    public string? Canonicalize(string payload, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (IsJsonContentType(contentType))
        {
            return CanonicalizeJson(payload);
        }

        return CanonicalizeText(payload);
    }

    /// <summary>
    /// Canonicalizes JSON payloads by sorting properties and removing ignored fields.
    /// </summary>
    /// <param name="payload">The JSON payload.</param>
    /// <returns>The canonical JSON payload or <c>null</c> when parsing fails.</returns>
    private string? CanonicalizeJson(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
            WriteCanonicalElement(writer, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a canonical JSON element with deterministic ordering.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="element">The element to canonicalize.</param>
    private void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (_options.IgnoredJsonFields.Contains(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Canonicalizes non-JSON payloads by trimming and collapsing whitespace.
    /// </summary>
    /// <param name="payload">The raw payload.</param>
    /// <returns>The canonical text payload.</returns>
    private static string CanonicalizeText(string payload)
    {
        var builder = new StringBuilder(payload.Length);
        var wasWhitespace = false;

        foreach (var character in payload)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!wasWhitespace)
                {
                    builder.Append(' ');
                    wasWhitespace = true;
                }

                continue;
            }

            wasWhitespace = false;
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Determines whether the content type represents JSON.
    /// </summary>
    /// <param name="contentType">The content type to evaluate.</param>
    /// <returns><c>true</c> if the content type is JSON; otherwise, <c>false</c>.</returns>
    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}
