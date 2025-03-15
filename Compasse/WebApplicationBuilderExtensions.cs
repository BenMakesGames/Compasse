namespace Compasse;

public static class WebApplicationBuilderExtensions
{
    private static readonly MethodRegistry MethodRegistry = new();

    public static WebApplicationBuilder AddCompasse(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IMethodRegistry>(MethodRegistry);

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

        return builder;
    }

    public static WebApplicationBuilder AddPrompt<TPrompt>(this WebApplicationBuilder builder) where TPrompt : IPrompt
    {
        // use reflection to check if the given tool implements IPrompt<TRequest, TResponse>
        // if it does, register it as a transient service
        var methodInterface = typeof(TPrompt).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPrompt<,>));

        if (methodInterface is null)
            throw new InvalidOperationException($"Prompt {typeof(TPrompt).Name} must implement IPrompt<TRequest, TResponse>.");

        MethodRegistry.RegisterPrompt<TPrompt>();

        builder.Services.AddTransient(methodInterface, typeof(TPrompt));

        return builder;
    }

    public static WebApplicationBuilder AddTool<TTool>(this WebApplicationBuilder builder) where TTool : ITool
    {
        // use reflection to check if the given tool implements ITool<TRequest, TResponse>
        // if it does, register it as a transient service
        var methodInterface = typeof(TTool).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITool<>));

        if (methodInterface is null)
            throw new InvalidOperationException($"Tool {typeof(TTool).Name} must implement IMethod<TRequest, TResponse>.");

        builder.Services.AddTransient(methodInterface, typeof(TTool));

        MethodRegistry.RegisterTool<TTool>();

        return builder;
    }

    public static WebApplication MapCompasse(this WebApplication app, string path = "/sse")
    {
        MethodRegistry.SetServiceProvider(app.Services);

        // Use CORS before mapping endpoints
        app.UseCors("CompasseCorsPolicy");

        app.Map(path, SseEndpoints.Handle);

        return app;
    }
}
