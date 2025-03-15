namespace Compasse.Methods;

public static class Initialize
{
    public static InitializeResponse Execute(InitializeRequest request)
    {
        return new()
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new()
            {
                Prompts = new()
                {
                    ListChanged = false, // TODO: would be nice to implement this!
                },
                Resources = new()
                {
                    Subscribe = false,
                    ListChanged = false, // TODO: would be nice to implement this!
                },
                Tools = new()
                {
                    ListChanged = false, // TODO: would be nice to implement this!
                }
            },
            ServerInfo = new() // TODO: pull from IConfiguration
            {
                Name = "Compasse",
                Version = "0.1.0",
            }
        };
    }
}

public sealed class InitializeRequest
{
    public required string ProtocolVersion { get; init; }
}

public sealed class InitializeResponse
{
    public required string ProtocolVersion { get; init; }
    public required InitializeResponseCapabilities Capabilities { get; init; }
    public required InitializeResponseServerInfo ServerInfo { get; init; }
}

public sealed class InitializeResponseCapabilities
{
    public required InitializeResponsePrompts Prompts { get; init; }
    public required InitializeResponseResources Resources { get; init; }
    public required InitializeResponseTools Tools { get; init; }
}

public sealed class InitializeResponsePrompts
{
    public required bool ListChanged { get; init; }
}

public sealed class InitializeResponseResources
{
    public required bool Subscribe { get; init; }
    public required bool ListChanged { get; init; }
}

public sealed class InitializeResponseTools
{
    public required bool ListChanged { get; init; }
}

public sealed class InitializeResponseServerInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
}
