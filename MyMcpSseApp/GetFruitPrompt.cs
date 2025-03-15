using Compasse;

namespace MyMcpSseApp;

public sealed class GetFruitPrompt: IPrompt<GetFruitPromptRequest, GetFruitPromptResponse>
{
    public static string Name => "get_fruit";
    public static string Description => "Gets a fruit.";

    public GetFruitPromptResponse Execute(GetFruitPromptRequest request)
    {
        // Return a response with a fruit
        return new GetFruitPromptResponse
        {
            Fruit = "Mango"
        };
    }
}

public sealed class GetFruitPromptRequest
{
}

public sealed class GetFruitPromptResponse
{
    public required string Fruit { get; init; }
}
