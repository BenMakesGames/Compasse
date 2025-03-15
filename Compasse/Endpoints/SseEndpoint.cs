using System.Text;
using System.Text.Json;
using Compasse.Model;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace Compasse.Endpoints;

internal static class SseEndpoint
{
    // Shared JsonRpc instance for handling requests from POST endpoints
    // and sending responses/notifications via SSE
    private static JsonRpc? JsonRpc;
    private static Stream? ClientStream;
    private static Stream? ServerStream;

    private static JsonSerializerOptions JsonSerializerOptions => new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    internal static async Task Handle(
        HttpContext context,
        IToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        CancellationToken ctx
    )
    {
        if(context.Request.Method == "GET")
        {
            await HandleGet(context, toolRegistry, serviceProvider, ctx);
        }
        else if (context.Request.Method == "POST")
        {
            await HandlePost(context, toolRegistry, serviceProvider, ctx);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Method not allowed", ctx);
        }
    }

    private static async Task HandleGet(
        HttpContext context,
        IToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        CancellationToken ctx
    )
    {
        Console.WriteLine($"SSE connection started: {context.Request.Method} {context.Request.Path}");

        try
        {
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
            var postEndpoint = $"{baseUrl}{context.Request.PathBase}{context.Request.Path}";

            var endpointEvent = $"event: endpoint\ndata: {postEndpoint}\n\n";
            var endpointBytes = Encoding.UTF8.GetBytes(endpointEvent);
            await context.Response.Body.WriteAsync(endpointBytes, 0, endpointBytes.Length, ctx);
            await context.Response.Body.FlushAsync(ctx);

            Console.WriteLine($"Endpoint event sent: {postEndpoint}");

            // Create a duplex stream for JSON-RPC if not already created
            if (JsonRpc == null)
            {
                var streams = FullDuplexStream.CreatePair();
                ClientStream = streams.Item1;
                ServerStream = streams.Item2;

                // Create a JsonRpc instance that communicates over the server stream
                JsonRpc = new JsonRpc(ServerStream);

                // Register methods from the tool registry
                foreach (var method in toolRegistry.Methods)
                {
                    var info = toolRegistry.GetToolMethod(serviceProvider, method);
                    JsonRpc.AddLocalRpcMethod(method, info.MethodInfo, info.Tool);
                }

                // Start listening for JSON-RPC messages
                JsonRpc.StartListening();
                Console.WriteLine("JsonRpc started listening");
            }

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

            // Start a task to read from the JsonRpc client stream and forward messages to the SSE connection
            var responseTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    Console.WriteLine("Starting to read JSON-RPC responses");
                    while (!ctx.IsCancellationRequested && ClientStream != null)
                    {
                        int bytesRead = await ClientStream.ReadAsync(buffer, 0, buffer.Length, ctx);
                        if (bytesRead == 0)
                        {
                            Console.WriteLine("End of JSON-RPC stream reached");
                            break; // End of stream
                        }

                        // Get the JSON-RPC message as a string
                        var jsonRpcMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Console.WriteLine($"Received JSON-RPC message: {jsonRpcMessage}");

                        // Format as SSE message event
                        var sseMessage = $"event: message\ndata: {jsonRpcMessage}\n\n";
                        var responseBytes = Encoding.UTF8.GetBytes(sseMessage);

                        await context.Response.Body.WriteAsync(responseBytes, 0, responseBytes.Length, ctx);
                        await context.Response.Body.FlushAsync(ctx);
                        Console.WriteLine("SSE message sent to client");
                    }
                }
                catch (Exception ex) when (!ctx.IsCancellationRequested)
                {
                    // Log unexpected errors
                    Console.WriteLine($"Error processing JSON-RPC response: {ex.Message}");

                    // Send error as SSE event
                    var errorMessage = $"event: message\ndata: {{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32000,\"message\":\"Internal error: {ex.Message}\"}}}}\n\n";
                    var errorBytes = Encoding.UTF8.GetBytes(errorMessage);
                    await context.Response.Body.WriteAsync(errorBytes, 0, errorBytes.Length, ctx);
                    await context.Response.Body.FlushAsync(ctx);
                }
                catch (Exception ex) when (ctx.IsCancellationRequested)
                {
                    // Expected when the client disconnects
                    Console.WriteLine($"JSON-RPC response processing cancelled: {ex.Message}");
                }
            }, ctx);

            try
            {
                // Wait for any task to complete or the connection to be cancelled
                await Task.WhenAny(responseTask, heartbeatTask);
                Console.WriteLine("One of the SSE tasks completed");
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error in SSE endpoint: {ex.Message}");
            throw;
        }
    }

    private static async Task HandlePost(
        HttpContext context,
        IToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        CancellationToken ctx
    )
    {
        Console.WriteLine($"POST request received: {context.Request.Path}");

        try
        {
            // Read the request body
            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync(ctx);
            Console.WriteLine($"Received request: {requestBody}");

            // Parse the JSON-RPC request
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestBody, JsonSerializerOptions);

            if (request == null)
            {
                await SendErrorResponse(context, -32700, "Parse error", null, ctx);
                return;
            }

            Console.WriteLine($"Processing method: {request.Method}");

            // Find the method in the tool registry
            var methodName = request.Method;
            var method = toolRegistry.Methods.FirstOrDefault(m => m == methodName);

            if (method == null)
            {
                await SendErrorResponse(context, -32601, "Method not found", request.Id, ctx);
                return;
            }

            // Get the method info and invoke it
            var info = toolRegistry.GetToolMethod(serviceProvider, method);

            try
            {
                var fullRequest = JsonSerializer.Deserialize(requestBody, info.RequestType, JsonSerializerOptions);
                object? @params = null;

                if (fullRequest is JsonRpcRequest<object> jsonRpcRequest)
                {
                    @params = jsonRpcRequest.Params;
                }

                // Invoke the method
                var result = info.MethodInfo.Invoke(info.Tool, [@params]);

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

                // Send the response
                await SendSuccessResponse(context, result, request.Id, ctx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking method: {ex.Message}");
                await SendErrorResponse(context, -32000, $"Error invoking method: {ex.Message}", request.Id, ctx);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing POST request: {ex.Message}");
            await SendErrorResponse(context, -32700, $"Parse error: {ex.Message}", null, ctx);
        }
    }

    private static async Task SendSuccessResponse<T>(HttpContext context, T result, int id, CancellationToken cancellationToken)
    {
        var response = new JsonRpcResponse<T>
        {
            JsonRpc = "2.0",
            Result = result,
            Id = id
        };

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, cancellationToken: cancellationToken);
    }

    private static async Task SendErrorResponse(HttpContext context, int code, string message, int? id, CancellationToken cancellationToken)
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

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, cancellationToken: cancellationToken);
    }

    public class JsonRpcResponse<T>
    {
        public required string JsonRpc { get; set; } = "2.0";
        public required T Result { get; init; }
        public required int Id { get; init; }
    }

    public class JsonRpcErrorResponse
    {
        public required string JsonRpc { get; set; } = "2.0";
        public required JsonRpcError Error { get; init; }
        public required int? Id { get; init; }
    }

    public class JsonRpcError
    {
        public required int Code { get; init; }
        public required string Message { get; init; }
        //public required object? Data { get; init; }
    }
}
