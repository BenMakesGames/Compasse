using Compasse;

namespace MyMcpSseApp;

public sealed class GetFruitTool: ITool<GetFruitToolRequest>
{
    public static string Name => "get_fruit";
    public static string Description => "Gets a fruit.";

    public ToolResponse Execute(GetFruitToolRequest request)
    {
        // Return a response with a fruit
        return new GetFruitToolResponse
        {
            Content = [
                new ToolResponseText("🥝")
            ]
        };
    }
}

public sealed class GetFruitToolRequest
{
}

public sealed class GetFruitToolResponse: ToolResponse
{
}
