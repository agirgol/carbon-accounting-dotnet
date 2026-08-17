using System.Collections.Generic;

namespace GhgAccounting.Generators.Json;

/// <summary>Base of the minimal JSON object model used to read catalog files.</summary>
internal abstract class JsonNode
{
    /// <summary>Zero-based character offset the node started at, for diagnostics.</summary>
    public int Start { get; set; }

    /// <summary>Character length of the node's source text, for diagnostics.</summary>
    public int Length { get; set; }
}

internal sealed class JsonObject : JsonNode
{
    private readonly Dictionary<string, JsonNode> _members = new Dictionary<string, JsonNode>();

    public IEnumerable<KeyValuePair<string, JsonNode>> Members => _members;

    public void Add(string name, JsonNode value) => _members[name] = value;

    public bool Contains(string name) => _members.ContainsKey(name);

    public JsonNode? Node(string name) =>
        _members.TryGetValue(name, out JsonNode? value) ? value : null;

    public string? String(string name) =>
        _members.TryGetValue(name, out JsonNode? value) && value is JsonString s ? s.Value : null;

    public double? Number(string name) =>
        _members.TryGetValue(name, out JsonNode? value) && value is JsonNumber n ? n.Value : (double?)null;

    public bool? Boolean(string name) =>
        _members.TryGetValue(name, out JsonNode? value) && value is JsonBoolean b ? b.Value : (bool?)null;

    public JsonArray? Array(string name) =>
        _members.TryGetValue(name, out JsonNode? value) ? value as JsonArray : null;

    public JsonObject? Object(string name) =>
        _members.TryGetValue(name, out JsonNode? value) ? value as JsonObject : null;
}

internal sealed class JsonArray : JsonNode
{
    public List<JsonNode> Items { get; } = new List<JsonNode>();
}

internal sealed class JsonString : JsonNode
{
    public JsonString(string value) => Value = value;

    public string Value { get; }
}

internal sealed class JsonNumber : JsonNode
{
    public JsonNumber(double value) => Value = value;

    public double Value { get; }
}

internal sealed class JsonBoolean : JsonNode
{
    public JsonBoolean(bool value) => Value = value;

    public bool Value { get; }
}

internal sealed class JsonNull : JsonNode
{
}
