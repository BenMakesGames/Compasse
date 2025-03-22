using System.Reflection;

namespace Compasse;

public interface IMethodRegistry
{
    IEnumerable<PromptInfo> Prompts { get; }
    IEnumerable<ResourceInfo> Resources { get; }
    IEnumerable<ToolInfo> Tools { get; }

    void RegisterPrompt<TPrompt>() where TPrompt : IPrompt;
    void RegisterResource<TResource>() where TResource : IResource;
    void RegisterTool<TTool>() where TTool : ITool;

    PromptCallable? GetPrompt(string promptName);
    // TODO: GetResource?
    ToolCallable? GetTool(string promptName);
}

public sealed class MethodRegistry: IMethodRegistry
{
    public IEnumerable<PromptInfo> Prompts => PromptRegistry.Values.AsEnumerable();
    public IEnumerable<ResourceInfo> Resources => ResourceRegistry.Values.AsEnumerable();
    public IEnumerable<ToolInfo> Tools => ToolRegistry.Values.AsEnumerable();

    private Dictionary<string, PromptInfo> PromptRegistry { get; } = [ ];
    private Dictionary<string, ResourceInfo> ResourceRegistry { get; } = [ ];
    private Dictionary<string, ToolInfo> ToolRegistry { get; } = [ ];

    private IServiceProvider? ServiceProvider { get; set; }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public void RegisterPrompt<TPrompt>() where TPrompt : IPrompt
    {
        if (PromptRegistry.ContainsKey(TPrompt.Name))
            throw new InvalidOperationException($"Prompt with name {TPrompt.Name} is already registered.");

        // get the object of type IPrompt<TRequest, TResponse>; if it's not one, throw an exception
        var methodInterface = typeof(TPrompt).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPrompt<,>))
            ?? throw new InvalidOperationException($"Prompt {typeof(TPrompt).Name} must implement IPrompt<TRequest, TResponse>.");

        // get the request and response types from the tool interface
        var genericArguments = methodInterface.GetGenericArguments();
        var requestType = genericArguments[0];
        var responseType = genericArguments[1];

        // get the Execute method info
        var methodInfo = methodInterface.GetMethod("Execute")
            ?? throw new InvalidOperationException($"Prompt {typeof(TPrompt).Name} must implement an Execute method.");

        PromptRegistry[TPrompt.Name] = new PromptInfo()
        {
            Name = TPrompt.Name,
            Description = TPrompt.Description,
            MethodInfo = methodInfo,
            MethodType = methodInterface,
            RequestType = requestType,
            ResponseType = responseType
        };

        Console.WriteLine("Registered prompt: " + TPrompt.Name);
    }

    public PromptCallable? GetPrompt(string promptName)
    {
        if (!PromptRegistry.TryGetValue(promptName, out var promptInfo))
            return null;

        // create an instance of the method
        var promptInstance = ServiceProvider!.GetService(promptInfo.MethodType)!;

        return new PromptCallable()
        {
            MethodInfo = promptInfo.MethodInfo,
            Prompt = promptInstance,
        };
    }

    public void RegisterResource<TResource>() where TResource : IResource
    {
        if (ResourceRegistry.ContainsKey(TResource.Name))
            throw new InvalidOperationException($"Resource with name {TResource.Name} is already registered.");

        // TODO: complete this!
    }

    public void RegisterTool<TTool>() where TTool : ITool
    {
        if (ToolRegistry.ContainsKey(TTool.Name))
            throw new InvalidOperationException($"Tool with name {TTool.Name} is already registered.");

        // get the object of type ITool<TRequest, TResponse>; if it's not one, throw an exception
        var methodInterface = typeof(TTool).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITool<>))
            ?? throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement ITool<TRequest>.");

        // get the request and response types from the tool interface
        var genericArguments = methodInterface.GetGenericArguments();
        var requestType = genericArguments[0];

        // get the Execute method info
        var methodInfo = methodInterface.GetMethod("Execute")
            ?? throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement an Execute method.");

        ToolRegistry[TTool.Name] = new ToolInfo()
        {
            Name = TTool.Name,
            Description = TTool.Description,
            MethodInfo = methodInfo,
            MethodType = methodInterface,
            RequestType = requestType,
        };

        Console.WriteLine("Registered tool: " + TTool.Name);
    }

    public ToolCallable? GetTool(string toolName)
    {
        if (!ToolRegistry.TryGetValue(toolName, out var toolInfo))
            return null;

        // create an instance of the method
        var toolInstance = ServiceProvider!.GetService(toolInfo.MethodType)!;

        return new ToolCallable()
        {
            MethodInfo = toolInfo.MethodInfo,
            Tool = toolInstance,
        };
    }
}

public sealed class ToolInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required MethodInfo MethodInfo { get; init; }
    public required Type MethodType { get; init; }
    public required Type RequestType { get; init; }
}

public sealed class ToolCallable
{
    public required MethodInfo MethodInfo { get; init; }
    public required object Tool { get; init; }
    public Type RequestType => MethodInfo.GetParameters()[0].ParameterType;
}

public sealed class ResourceInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public sealed class PromptInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required MethodInfo MethodInfo { get; init; }
    public required Type MethodType { get; init; }
    public required Type RequestType { get; init; }
    public required Type ResponseType { get; init; }
}

public sealed class PromptCallable
{
    public required MethodInfo MethodInfo { get; init; }
    public required object Prompt { get; init; }
    public Type RequestType => MethodInfo.GetParameters()[0].ParameterType;
    public Type ResponseType => MethodInfo.ReturnType;
}
