// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

using Microsoft.Agents.A365.DevTools.MockToolingServer;

/// <summary>
/// An IServerService implementation for running the MockToolingServer
/// </summary>
public class MockToolingServerService : IServerService
{
    /// <inheritdoc/>
    public async Task StartAsync(string[] args)
    {
        await Server.Start(args);
    }
}