using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compasse.Model;

public class JsonRpcMessage
{
    public required string JsonRpc { get; init; }
    public required string Method { get; init; }
}

public class JsonRpcRequest: JsonRpcMessage
{
    [JsonConverter(typeof(IdConverter))]
    public required string Id { get; init; }

    public JsonElement? Params { get; init; }
}

public class JsonRpcToolParams
{
    public required string Name { get; init; }
    public required JsonElement? Arguments { get; init; }
}

public class JsonRpcPromptParams
{
    public required string Name { get; init; }
    public required JsonElement? Arguments { get; init; }
}
