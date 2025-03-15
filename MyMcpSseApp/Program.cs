using Compasse;
using MyMcpSseApp;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCompasse()
    .AddPrompt<GetFruitPrompt>()
    .AddTool<GetFruitTool>()
;

var app = builder.Build();

app.MapCompasse();

await app.RunAsync();
