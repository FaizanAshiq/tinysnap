using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tinysnap.Core;

/// <summary>Reads JSON the way the Swift decoders do: each key on its own, a missing or
/// wrong-typed value coming back as null for the caller's fallback, never as an error.</summary>
public static class Json
{
    public static JsonNode? Parse(byte[] utf8)
    {
        try { return JsonNode.Parse(utf8); }
        catch (JsonException) { return null; }
    }

    public static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    /// <summary>A number, whole or not. The Mac writes whole numbers without a point.</summary>
    public static double? Number(JsonObject o, string key) => Number(o[key]);

    /// <summary>Null for anything but a finite number. .NET reads 1e400 as infinity, which the
    /// Mac's decoder refuses, and one infinite value breaks every sum it reaches. Read from the
    /// number's text, so a node built in memory from an int or a uint reads as well as a parsed
    /// one; asked for a double, those gave nothing.</summary>
    public static double? Number(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
        && double.IsFinite(d) ? d : null;

    public static int? Integer(JsonObject o, string key) =>
        Number(o, key) is { } d && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;

    public static bool? Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

    public static string? String(JsonObject o, string key) =>
        o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public static JsonObject? Object(JsonObject o, string key) => o[key] as JsonObject;

    public static JsonArray? Array(JsonObject o, string key) => o[key] as JsonArray;

    /// <summary>True when the key is there and holds JSON null, which some settings read
    /// differently from a missing key.</summary>
    public static bool IsNull(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var node) && node is null;

    /// <summary>An enum case by the name the Mac writes: the Swift case name, lower camel case.</summary>
    public static T? Enum<T>(JsonObject o, string key) where T : struct, System.Enum =>
        String(o, key) is { } name ? FromWire<T>(name) : null;

    public static T? FromWire<T>(string name) where T : struct, System.Enum =>
        System.Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => Wire(value!.Value) == name);

    /// <summary>A case's wire name: <c>ExtraSmall</c> is written <c>extraSmall</c>, as Swift's
    /// raw values are.</summary>
    public static string Wire<T>(T value) where T : struct, System.Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>An id as the Mac writes it: uppercase, with dashes.</summary>
    public static string Uuid(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>Keys sorted at every level and text left as UTF-8, as the Mac's encoder writes
    /// them. The relaxed encoder only stops escaping characters that matter inside HTML, and
    /// these files are never put in a page.</summary>
    public static string Write(JsonNode node, bool indented = true) =>
        Sorted(node)!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public static byte[] WriteBytes(JsonNode node, bool indented = true) =>
        System.Text.Encoding.UTF8.GetBytes(Write(node, indented));

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };
}
