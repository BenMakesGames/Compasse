using System.Reflection;
using Compasse.Tools;

namespace Compasse;

public interface IToolRegistry
{
    IEnumerable<string> Methods { get; }
    void RegisterTool<TTool>() where TTool : ITool;
    (MethodInfo MethodInfo, object Tool, Type RequestType) GetToolMethod(IServiceProvider serviceProvider, string methodName);
}

public sealed class ToolRegistry: IToolRegistry
{
    public IEnumerable<string> Methods => Tools.Keys.AsEnumerable();
    public Dictionary<string, (MethodInfo MethodInfo, Type ToolType, Type RequestType, Type ResponseType)> Tools { get; } = [ ];

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

        Tools[TTool.Method] = (methodInfo, toolInterface, requestType, responseType);

        Console.WriteLine("Registered tool: " + TTool.Method);
    }

    public (MethodInfo MethodInfo, object Tool, Type RequestType) GetToolMethod(IServiceProvider serviceProvider, string methodName)
    {
        if (!Tools.TryGetValue(methodName, out var method))
            throw new InvalidOperationException($"Tool with name {methodName} is not registered.");

        // create an instance of the tool
        var toolInstance = serviceProvider.GetService(method.ToolType)!;

        return (method.MethodInfo, toolInstance, method.RequestType);
    }
}
