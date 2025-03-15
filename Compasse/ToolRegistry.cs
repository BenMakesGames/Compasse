using System.Reflection;
using Compasse.Tools;

namespace Compasse;

public interface IToolRegistry
{
    IEnumerable<ToolInfo> Methods { get; }
    void RegisterTool<TTool>() where TTool : ITool;
    ToolMethod? GetToolMethod(IServiceProvider serviceProvider, string methodName);
}

public sealed class ToolRegistry: IToolRegistry
{
    public IEnumerable<ToolInfo> Methods => Tools.Values.AsEnumerable();
    public Dictionary<string, ToolInfo> Tools { get; } = [ ];

    public void RegisterTool<TTool>() where TTool : ITool
    {
        if (Tools.ContainsKey(TTool.Method))
            throw new InvalidOperationException($"Tool with name {TTool.Method} is already registered.");

        // get the object of type TTool<TRequest, TResponse>; if it's not one, throw an exception
        var toolInterface = typeof(TTool).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITool<,>))
            ?? throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement ITool<TRequest, TResponse>.");

        // get the request and response types from the tool interface
        var genericArguments = toolInterface.GetGenericArguments();
        var requestType = genericArguments[0];
        var responseType = genericArguments[1];

        // get the Handle method info
        var methodInfo = toolInterface.GetMethod("Execute")
            ?? throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement a Execute method.");

        Tools[TTool.Method] = new ToolInfo()
        {
            Method = TTool.Method,
            Description = TTool.Description,
            MethodInfo = methodInfo,
            ToolType = toolInterface,
            RequestType = requestType,
            ResponseType = responseType
        };

        Console.WriteLine("Registered tool: " + TTool.Method);
    }

    public ToolMethod? GetToolMethod(IServiceProvider serviceProvider, string methodName)
    {
        if (!Tools.TryGetValue(methodName, out var method))
            return null;

        // create an instance of the tool
        var toolInstance = serviceProvider.GetService(method.ToolType)!;

        return new ToolMethod()
        {
            MethodInfo = method.MethodInfo,
            Tool = toolInstance,
            RequestType = method.RequestType
        };
    }
}

public sealed class ToolInfo
{
    public required string Method { get; init; }
    public required string Description { get; init; }
    public required MethodInfo MethodInfo { get; init; }
    public required Type ToolType { get; init; }
    public required Type RequestType { get; init; }
    public required Type ResponseType { get; init; }
}

public sealed class ToolMethod
{
    public required MethodInfo MethodInfo { get; init; }
    public required object Tool { get; init; }
    public required Type RequestType { get; init; }
}
