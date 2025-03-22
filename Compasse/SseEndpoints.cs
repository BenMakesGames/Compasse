using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Compasse.Methods;
using Compasse.Model;

namespace Compasse;

internal static class SseEndpoints
{
    // Thread-safe dictionary to store SSE response streams for each client
    private static readonly ConcurrentDictionary<string, Stream> ClientStreams = new();

    // For SSE responses specifically, we want to avoid newlines in the JSON
    private static JsonSerializerOptions SseJsonSerializerOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    internal static async Task Handle(
        HttpContext context,
        IMethodRegistry methodRegistry,
        IServiceProvider serviceProvider,
        CancellationToken ctx
    )
    {
        if(context.Request.Method == "GET")
        {
            await HandleGet(context, ctx);
        }
        else if (context.Request.Method == "POST")
        {
            await HandlePost(context, methodRegistry, ctx);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Method not allowed", ctx);
        }
    }

    private static async Task HandleGet(
        HttpContext context,
        CancellationToken ctx
    )
    {
        // Generate a unique client ID
        var clientId = Guid.NewGuid().ToString();
        Console.WriteLine($"SSE connection started for client {clientId}: {context.Request.Method} {context.Request.Path}");

        try
        {
            // Store the client's response stream
            ClientStreams.TryAdd(clientId, context.Response.Body);

            // Set headers for SSE
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            // Send an initial comment to establish the connection
            var initialMessage = ": connected\n\n";
            var initialBytes = Encoding.UTF8.GetBytes(initialMessage);
            await context.Response.Body.WriteAsync(initialBytes, 0, initialBytes.Length, ctx);
            await context.Response.Body.FlushAsync(ctx);

            Console.WriteLine("Initial SSE comment sent");

            // Send the required 'endpoint' event according to MCP specification
            // This tells the client where to send POST requests
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var postEndpoint = $"{baseUrl}{context.Request.PathBase}{context.Request.Path}?clientId={clientId}";

            var endpointEvent = $"event: endpoint\ndata: {postEndpoint}\n\n";
            var endpointBytes = Encoding.UTF8.GetBytes(endpointEvent);
            await context.Response.Body.WriteAsync(endpointBytes, 0, endpointBytes.Length, ctx);
            await context.Response.Body.FlushAsync(ctx);

            Console.WriteLine($"Endpoint event sent: {postEndpoint}");

            // Send a heartbeat every 15 seconds to keep the connection alive
            var heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    int heartbeatCount = 0;
                    while (!ctx.IsCancellationRequested)
                    {
                        await Task.Delay(15000, ctx); // 15 seconds

                        // Send a comment as heartbeat
                        heartbeatCount++;
                        var heartbeat = $": heartbeat {heartbeatCount}\n\n";
                        var heartbeatBytes = Encoding.UTF8.GetBytes(heartbeat);

                        await context.Response.Body.WriteAsync(heartbeatBytes, 0, heartbeatBytes.Length, ctx);
                        await context.Response.Body.FlushAsync(ctx);
                        Console.WriteLine($"Heartbeat {heartbeatCount} sent");
                    }
                }
                catch (OperationCanceledException) when (ctx.IsCancellationRequested)
                {
                    // Expected when the client disconnects
                    Console.WriteLine("Heartbeat task cancelled");
                }
            }, ctx);

            try
            {
                // Wait for the heartbeat task to complete or the connection to be cancelled
                await heartbeatTask;
                Console.WriteLine("SSE connection ended");
            }
            catch (OperationCanceledException) when (ctx.IsCancellationRequested)
            {
                // Expected when the client disconnects
                Console.WriteLine("SSE connection cancelled");
            }
            catch (Exception ex)
            {
                // Log unexpected errors
                Console.WriteLine($"Unexpected error in SSE endpoint: {ex.Message}");
            }
            finally
            {
                // Remove the client's stream when the connection ends
                ClientStreams.TryRemove(clientId, out _);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error in SSE endpoint: {ex.Message}");
            throw;
        }
    }

    private static async Task HandlePost(
        HttpContext context,
        IMethodRegistry methodRegistry,
        CancellationToken ctx
    )
    {
        Console.WriteLine($"POST request received: {context.Request.Path}");

        try
        {
            // Get the client ID from the query string
            var clientId = context.Request.Query["clientId"].ToString();
            if (string.IsNullOrEmpty(clientId) || !ClientStreams.TryGetValue(clientId, out var clientStream))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid or missing clientId", ctx);
                return;
            }

            // Read the request body
            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync(ctx);
            Console.WriteLine($"Received request: {requestBody}");

            // first, try to parse it as a full request
            var request = TryToDeserialize<JsonRpcRequest>(requestBody);

            if (request is null)
            {
                // no? maybe it's just a message:
                var message = TryToDeserialize<JsonRpcMessage>(requestBody);

                if (message is null)
                {
                    // still no? we don't know what this thing is, then:
                    await SendErrorResponseSse(clientStream, -32700, "Parse error", null, ctx);
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                // it was a message, but not a full method request...
                // TODO: maybe we'd care to note this for later; for now, just return:
                return;
            }

            Console.WriteLine($"Processing method: {request.Method}");

            try
            {
                var result = request.Method switch
                {
                    "initialize" => Invoke<InitializeRequest, InitializeResponse>(Initialize.Execute, request, () => throw new ArgumentException()),
                    "prompts/list" => Invoke<PromptsListRequest, PromptsListResponse>(r => PromptsList.Execute(methodRegistry, r), request, () => new()),
                    "prompts/get" => await InvokePrompt(methodRegistry, request, ctx),

                    "resources/list" => null, // TODO: implement
                    //Invoke<ResourcesListRequest, ResourcesResponseRequest>(ResourcesList.Execute, request);

                    "resources/read" => null, // TODO: implement
                    "tools/list" => Invoke<ToolsListRequest, ToolsListResponse>(r => ToolsList.Execute(methodRegistry, r), request, () => new()),
                    "tools/call" => await InvokeTool(methodRegistry, request, ctx),
                };

                await SendSuccessResponseSse(clientStream, result, request.Id, ctx);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking method: {ex.Message}");
                await SendErrorResponseSse(clientStream, -32000, $"Error invoking method: {ex.Message}", request.Id, ctx);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing POST request: {ex.Message}");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Error processing request: {ex.Message}", ctx);
        }
    }

    private static TResponse Invoke<TRequest, TResponse>(Func<TRequest, TResponse> method, JsonRpcRequest request, Func<TRequest> defaultRequest)
        where TRequest: class
    {
        var @params = request.Params is { } paramsJson
            ? paramsJson.Deserialize<TRequest>(SseJsonSerializerOptions)
            : null;

        return method(@params ?? defaultRequest());
    }

    private static async Task<object?> InvokePrompt(IMethodRegistry methodRegistry, JsonRpcRequest request, CancellationToken ctx)
    {
        if (request.Params is not {} paramsJson)
            throw new ArgumentException("Params must not be null.");

        if(paramsJson.Deserialize<JsonRpcPromptParams>(SseJsonSerializerOptions) is not {} @params)
            throw new ArgumentException("Failed to deserialize params.");

        var promptInfo = methodRegistry.GetPrompt(@params.Name);

        if (promptInfo is null)
            throw new InvalidOperationException($"There is no prompt named {@params.Name}.");

        var arguments = @params.Arguments?.Deserialize(promptInfo.RequestType, SseJsonSerializerOptions);

        // Invoke the method
        var result = promptInfo.MethodInfo.Invoke(promptInfo.Prompt, [arguments]);

        // Handle async results
        if (result is Task task)
        {
            await task;

            // Get the result from the task if it's a generic Task<T>
            if (task.GetType().IsGenericType)
            {
                var resultProperty = task.GetType().GetProperty("Result");
                result = resultProperty?.GetValue(task);
            }
            else
            {
                result = null;
            }
        }

        return result;
    }

    private static async Task<object?> InvokeTool(IMethodRegistry methodRegistry, JsonRpcRequest request, CancellationToken ctx)
    {
        if (request.Params is not {} paramsJson)
            throw new ArgumentException("Params must not be null.");

        if(paramsJson.Deserialize<JsonRpcToolParams>(SseJsonSerializerOptions) is not {} @params)
            throw new ArgumentException("Failed to deserialize params.");

        var toolInfo = methodRegistry.GetTool(@params.Name);

        if (toolInfo is null)
            throw new InvalidOperationException($"There is no tool named {@params.Name}.");

        var arguments = @params.Arguments?.Deserialize(toolInfo.RequestType, SseJsonSerializerOptions);

        // Invoke the method
        var result = toolInfo.MethodInfo.Invoke(toolInfo.Tool, [arguments]);

        // Handle async results
        if (result is Task task)
        {
            await task;

            // Get the result from the task if it's a generic Task<T>
            if (task.GetType().IsGenericType)
            {
                var resultProperty = task.GetType().GetProperty("Result");
                result = resultProperty?.GetValue(task);
            }
            else
            {
                result = null;
            }
        }

        return result;
    }

    private static async Task SendSuccessResponseSse<T>(Stream clientStream, T result, string id, CancellationToken cancellationToken)
    {
        var response = new JsonRpcResponse<T>
        {
            JsonRpc = "2.0",
            Result = result,
            Id = id
        };

        // First serialize the response to JSON without indentation
        var json = JsonSerializer.Serialize(response, SseJsonSerializerOptions);

        // Create the complete SSE message with explicit line endings
        var sseMessage = $"event: message\ndata: {json}\n\n";

        // Convert to bytes and write in a single operation
        var messageBytes = Encoding.UTF8.GetBytes(sseMessage);
        Console.WriteLine($"Sending SSE success message (length: {messageBytes.Length} bytes)");

        await clientStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await clientStream.FlushAsync(cancellationToken);

        Console.WriteLine($"Sent success response for request {id}: {json}");
    }

    private static async Task SendErrorResponseSse(Stream clientStream, int code, string message, string? id, CancellationToken cancellationToken)
    {
        var response = new JsonRpcErrorResponse
        {
            JsonRpc = "2.0",
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            },
            Id = id
        };

        // First serialize the response to JSON without indentation
        var json = JsonSerializer.Serialize(response, SseJsonSerializerOptions);

        // Create the complete SSE message with explicit line endings
        var sseMessage = $"event: message\ndata: {json}\n\n";

        // Convert to bytes and write in a single operation
        var messageBytes = Encoding.UTF8.GetBytes(sseMessage);
        Console.WriteLine($"Sending SSE error message (length: {messageBytes.Length} bytes)");

        await clientStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await clientStream.FlushAsync(cancellationToken);

        Console.WriteLine($"Sent error response for request {id}: {json}");
    }

    private static T? TryToDeserialize<T>(string text) where T: class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, SseJsonSerializerOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
