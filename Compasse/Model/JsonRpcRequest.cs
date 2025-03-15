namespace Compasse.Model;

public class JsonRpcRequest
{
    public required string JsonRpc { get; init; }
    public required string Method { get; init; }
    public required int Id { get; init; }
}

public class JsonRpcRequest<T> : JsonRpcRequest
{
    public required T Params { get; init; }
}
