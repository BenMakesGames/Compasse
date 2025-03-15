using System.Text;
using System.Text.Json;
using Compasse.Model;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Compasse.Endpoints;

internal static class SseEndpoint
{
    // Thread-safe dictionary to store SSE response streams for each client
    private static readonly ConcurrentDictionary<string, Stream> _clientStreams = new();

    private static JsonSerializerOptions JsonSerializerOptions => new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // For SSE responses specifically, we want to avoid newlines in the JSON
    private static JsonSerializerOptions SseJsonSerializerOptions => new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
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
        // Generate a unique client ID
        var clientId = Guid.NewGuid().ToString();
        Console.WriteLine($"SSE connection started for client {clientId}: {context.Request.Method} {context.Request.Path}");

        try
        {
            // Store the client's response stream
            _clientStreams.TryAdd(clientId, context.Response.Body);

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
                _clientStreams.TryRemove(clientId, out _);
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
            // Get the client ID from the query string
            var clientId = context.Request.Query["clientId"].ToString();
            if (string.IsNullOrEmpty(clientId) || !_clientStreams.TryGetValue(clientId, out var clientStream))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid or missing clientId", ctx);
                return;
            }

            // Read the request body
            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync(ctx);
            Console.WriteLine($"Received request: {requestBody}");

            // Parse the JSON-RPC request
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestBody, JsonSerializerOptions);

            if (request == null)
            {
                await SendErrorResponseSse(clientStream, -32700, "Parse error", null, ctx);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            Console.WriteLine($"Processing method: {request.Method}");

            // Get the method info and invoke it
            var info = toolRegistry.GetToolMethod(serviceProvider, request.Method);

            if (info is null)
            {
                await SendErrorResponseSse(clientStream, -32601, "Method not found", request.Id, ctx);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

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

                // Send the response through SSE
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

    private static async Task SendSuccessResponseSse<T>(Stream clientStream, T result, int id, CancellationToken cancellationToken)
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
        Console.WriteLine($"Sending SSE message (length: {messageBytes.Length} bytes):");
        Console.WriteLine(BitConverter.ToString(messageBytes));

        await clientStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await clientStream.FlushAsync(cancellationToken);

        Console.WriteLine($"Sent success response for request {id}: {json}");
    }

    private static async Task SendErrorResponseSse(Stream clientStream, int code, string message, int? id, CancellationToken cancellationToken)
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
        Console.WriteLine($"Sending SSE message (length: {messageBytes.Length} bytes):");
        Console.WriteLine(BitConverter.ToString(messageBytes));

        await clientStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await clientStream.FlushAsync(cancellationToken);

        Console.WriteLine($"Sent error response for request {id}: {json}");
    }

    public class JsonRpcResponse<T>
    {
        [JsonPropertyName("jsonrpc")]
        public required string JsonRpc { get; set; } = "2.0";
        public required T Result { get; init; }
        public required int Id { get; init; }
    }

    public class JsonRpcErrorResponse
    {
        [JsonPropertyName("jsonrpc")]
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
