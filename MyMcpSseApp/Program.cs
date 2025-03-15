using Compasse;
using MyMcpSseApp;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCompasse()
    .AddTool<GetFruit>()
;

var app = builder.Build();

app.MapCompasse();

await app.RunAsync();
