# Compasse

**Compasse** is an MCP SSE framework for .NET. (Yes, the "m" and "c" are a little mixed up there. I tried my best.)

Here is what your `Program.cs` will look like when using **Compasse**:

```c#
using Compasse;
using MyMcpSseApp;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCompasse() // add Compasse
    .AddTool<GetFruit>() // add a tool you made!
;

var app = builder.Build();

app.MapCompasse(); // map Compasse endpoints

await app.RunAsync();
```

And here is what that tool might look like:

```c#
using Compasse.Tools;

namespace MyMcpSseApp;

public sealed class GetFruit: ITool<GetFruitRequest, GetFruitResponse>
{
    public static string Method => "prompts/fruit";
    public static string Description => "Gets a fruit.";

    // if you need some services, inject 'em with constructor DI
    
    public GetFruitResponse Execute(GetFruitRequest request)
    {
        // Return a response with a fruit
        return new GetFruitResponse
        {
            Fruit = "Mango"
        };
    }
}

public sealed class GetFruitRequest
{
    // if your prompt has arguments, they go here
}

public sealed class GetFruitResponse
{
    public required string Fruit { get; init; }
}
```

Once that's up and running, point your AI agent at it (when running locally, that's typically http://localhost:5000/sse), and it will now have access to `prompts/fruit`.

### Q&A

(Q&A last updated on 2024-03-14.)

**Q. How do you say "compasse"?**<br>
A. I can't see that it matters. But if you like feeling fancy, you can try and put a French spin on it.

**Q. Is it AOT ready?**<br>
A. I can't imagine it is. But if you like feeling fancy, please make a positive PR. (I think it might be a lot of work, but I'm not as handy with source generators as you might be.)

**Q. Is it production ready?**<br>
A. I can't imagine it is. But if you like feeling fancy, please make a positive PR.

**Q. You did something ridiculous, and I hate it.**<br>
A. That's not a question. But you're probably right: I learned SSE and JSON-RPC, like, 2 days ago. BUT - can you guess where this is going? - if you like feeling fancy, please make a positive PR.
