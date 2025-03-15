using Compasse.Endpoints;
using Compasse.Tools;

namespace Compasse;

public static class WebApplicationBuilderExtensions
{
    private static readonly IToolRegistry ToolRegistry = new ToolRegistry();

    public static WebApplicationBuilder AddCompasse(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(ToolRegistry);

        // Add CORS support with specific allowed origins
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("CompasseCorsPolicy", policy =>
            {
                policy
                    .SetIsOriginAllowed(_ => true) // For development - replace with specific origins in production
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("Content-Type", "Content-Length")
                    .AllowCredentials(); // Required for EventSource in some browsers
            });
        });

        builder.AddTool<PromptsList>();

        return builder;
    }

    public static WebApplicationBuilder AddTool<TTool>(this WebApplicationBuilder builder) where TTool : ITool
    {
        // use reflection to check if the given tool implements ITool<TRequest, TResponse>
        // if it does, register it as a transient service
        var toolInterface = typeof(TTool).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITool<,>));

        if (toolInterface is null)
            throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement ITool<TRequest, TResponse>.");

        builder.Services.AddTransient(toolInterface, typeof(TTool));

        ToolRegistry.RegisterTool<TTool>();

        return builder;
    }

    public static WebApplication MapCompasse(this WebApplication app, string path = "/sse")
    {
        // Use CORS before mapping endpoints
        app.UseCors("CompasseCorsPolicy");

        app.Map(path, SseEndpoint.Handle);

        return app;
    }
}
