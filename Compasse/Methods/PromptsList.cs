namespace Compasse.Methods;

public static class PromptsList
{
    public static PromptsListResponse Execute(IMethodRegistry methodRegistry, PromptsListRequest request)
    {
        var prompts = methodRegistry.Prompts
            .Select(p => new Prompt
            {
                Name = p.Name,
                Description = p.Description
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
