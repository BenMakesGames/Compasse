namespace Compasse.Tools;

public sealed class PromptsList(IToolRegistry toolRegistry): ITool<PromptsListRequest, PromptsListResponse>
{
    public static string Method => "prompts/list";
    public static string Description => "Lists all available prompts.";

    public PromptsListResponse Execute(PromptsListRequest request)
    {
        var prompts = toolRegistry.Methods
            .Select(method => new Prompt
            {
                Name = method.Method,
                Description = method.Description
            })
            .ToList();

        return new()
        {
            Prompts = prompts
        };
    }
}

public sealed class PromptsListRequest
{
    public string? Cursor { get; set; }
}

public sealed class PromptsListResponse
{
    public required List<Prompt> Prompts { get; set; } = new();
}

public sealed class Prompt
{
    public required string Name { get; set; }
    public required string Description { get; set; }
}
