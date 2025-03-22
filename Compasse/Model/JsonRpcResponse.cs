using System.Text.Json.Serialization;

namespace Compasse.Model;

public class JsonRpcResponse<T>
{
    [JsonPropertyName("jsonrpc")]
    public required string JsonRpc { get; set; } = "2.0";
    public required T Result { get; init; }
    public required string Id { get; init; }
}
