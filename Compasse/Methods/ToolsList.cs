namespace Compasse.Methods;

public static class ToolsList
{
    public static ToolsListResponse Execute(IMethodRegistry methodRegistry, ToolsListRequest request)
    {
        // TODO: support pagination

        var tools = methodRegistry.Tools
            .Select(t => new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = new()
                {
                    // TODO: complete this (use reflection to describe t.RequestType)
                    Type = "object",
                    Properties = new(),
                    Required = [],
                }
            })
            .ToList();

        return new()
        {
            Tools = tools
        };
    }
}

public sealed class ToolsListRequest
{
    public string? Cursor { get; set; }
}

public sealed class ToolsListResponse
{
    public required List<Tool> Tools { get; set; } = new();
}

public sealed class Tool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required ToolInputSchema InputSchema { get; set; }
}

public sealed class ToolInputSchema
{
    public required string Type { get; set; }
    public required Dictionary<string, ToolInputProperty> Properties { get; set; }
    public required string[] Required { get; set; }
}

public sealed class ToolInputProperty
{
    public required string Type { get; set; }
    public required string Description { get; set; }
}
