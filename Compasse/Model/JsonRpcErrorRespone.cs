using System.Text.Json.Serialization;

namespace Compasse.Model;

public class JsonRpcErrorResponse
{
    [JsonPropertyName("jsonrpc")]
    public required string JsonRpc { get; set; } = "2.0";
    public required JsonRpcError Error { get; init; }
    public required string? Id { get; init; }
}

public class JsonRpcError
{
    public required int Code { get; init; }
    public required string Message { get; init; }
    //public required object? Data { get; init; }
}
