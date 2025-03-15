using Compasse.Tools;

namespace MyMcpSseApp;

public sealed class GetFruit: ITool<GetFruitRequest, GetFruitResponse>
{
    public static string Method => "prompts/fruit";
    public static string Description => "Gets a fruit.";

    public GetFruitResponse Execute(GetFruitRequest request)
    {
        // Return a response with a fruit
        return new GetFruitResponse
        {
            Fruit = "Mango"
        };
    }
}

public sealed class GetFruitRequest
{
}

public sealed class GetFruitResponse
{
    public required string Fruit { get; init; }
}
