// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

public class BotConfiguratorTests
{
    private readonly ILogger<BotConfigurator> _logger;
    private readonly CommandExecutor _executor;
    private readonly BotConfigurator _configurator;
    private readonly IConfigService _configService;
    private readonly AuthenticationService _authService;

    public BotConfiguratorTests()
    {
        _logger = Substitute.For<ILogger<BotConfigurator>>();
        _executor = Substitute.For<CommandExecutor>(Substitute.For<ILogger<CommandExecutor>>());
        _configService = Substitute.For<IConfigService>();
        _authService = Substitute.For<AuthenticationService>(Substitute.For<ILogger<AuthenticationService>>());
        _configurator = new BotConfigurator(_logger, _executor, _configService, _authService);
    }



    [Fact]
    public async Task CreateOrUpdateBotWithAgentBlueprintAsync_IdentityDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var botName = "test-bot";
        var location = "westus2";
        var messagingEndpoint = "https://test.azurewebsites.net/api/messages";
        var description = "Test Bot Description";
        var agentBlueprintId = "test-agent-blueprint-id";

        var subscriptionResult = new CommandResult { ExitCode = 1, StandardError = "Subscription not found" };

        _executor.ExecuteAsync(
            Arg.Is("az"),
            Arg.Is<string>(s => s.Contains("account show")),
            Arg.Any<string?>(),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(subscriptionResult));

        // Act
        var result = await _configurator.CreateEndpointWithAgentBlueprintAsync(
            botName, location, messagingEndpoint, description, agentBlueprintId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateOrUpdateBotWithAgentBlueprintAsync_BotCreationSucceeds_ReturnsTrue()
    {
        // Arrange
        var botName = "test-bot";
        var resourceGroupName = "test-resource-group";
        var location = "westus2";
        var messagingEndpoint = "https://test.azurewebsites.net/api/messages";
        var description = "Test Bot Description";
        var agentBlueprintId = "test-agent-blueprint-id";

        var subscriptionResult = new CommandResult 
        { 
            ExitCode = 0, 
            StandardOutput = """
                {
                  "tenantId": "test-tenant-id"
                }
                """ 
        };

        var botCheckResult = new CommandResult { ExitCode = 1, StandardError = "Bot not found" };
        var botCreateResult = new CommandResult 
        { 
            ExitCode = 0, 
            StandardOutput = """{"name": "test-bot"}""" 
        };

        _executor.ExecuteAsync(
            Arg.Is("az"),
            Arg.Is<string>(s => s.Contains("account show")),
            Arg.Any<string?>(),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(subscriptionResult));

        _executor.ExecuteAsync(
            Arg.Is("az"),
            Arg.Is<string>(s => s.Contains($"bot show --name {botName} --resource-group {resourceGroupName}")),
            Arg.Any<string?>(),
            Arg.Is(true),
            Arg.Is(true), // suppressErrorLogging: true (bot doesn't exist is expected)
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(botCheckResult));

        _executor.ExecuteAsync(
            Arg.Is("az"),
            Arg.Is<string>(s => s.Contains($"bot create --resource-group {resourceGroupName} --name {botName}")),
            Arg.Any<string?>(),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(botCreateResult));

        // Act
        var result = await _configurator.CreateEndpointWithAgentBlueprintAsync(
            botName,  location, messagingEndpoint, description, agentBlueprintId);

        // Assert
        Assert.True(result);
    }
}
