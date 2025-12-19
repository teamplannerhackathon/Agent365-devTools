namespace Microsoft.Agents.A365.DevTools.Cli.Services;

using Microsoft.Agents.A365.DevTools.MockToolingServer;

public class ServerService : IServerService
{
    /// <inheritdoc/>
    public async Task StartAsync(string[] args)
    {
        await Server.Start(args);
    }
}