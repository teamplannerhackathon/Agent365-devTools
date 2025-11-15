// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace Microsoft.Agents.A365.DevTools.Cli.Services
{
    public interface IBotConfigurator
    {
        Task<bool> CreateEndpointWithAgentBlueprintAsync(string endpointName, string location, string messagingEndpoint, string agentDescription, string agentBlueprintId);
        Task<bool> DeleteEndpointWithAgentBlueprintAsync(string endpointName, string location, string agentBlueprintId);
    }
}