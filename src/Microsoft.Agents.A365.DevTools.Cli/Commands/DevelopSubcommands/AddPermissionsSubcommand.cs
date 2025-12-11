// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Helpers;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text.Json;

namespace Microsoft.Agents.A365.DevTools.Cli.Commands.DevelopSubcommands;

/// <summary>
/// AddPermissions subcommand - Adds MCP server API permissions to a custom application
/// </summary>
internal static class AddPermissionsSubcommand
{
    public static Command CreateCommand(
        ILogger logger,
        IConfigService configService,
        GraphApiService graphApiService)
    {
        var command = new Command(
            "addpermissions",
            "Add MCP server API permissions to a custom application");

        var configOption = new Option<FileInfo>(
            ["--config", "-c"],
            getDefaultValue: () => new FileInfo("a365.config.json"),
            description: "Configuration file path");

        var manifestOption = new Option<FileInfo?>(
            ["--manifest", "-m"],
            description: "Path to ToolingManifest.json (defaults to current directory)");

        var appIdOption = new Option<string>(
            ["--app-id"],
            description: "Application (client) ID to add permissions to. If not specified, uses the clientAppId from config")
        {
            IsRequired = false
        };

        var scopesOption = new Option<string[]?>(
            ["--scopes"],
            description: "Specific scopes to add (e.g., McpServers.Mail.All McpServers.Calendar.All). If not specified, uses all scopes from ToolingManifest.json")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            description: "Show detailed output");

        var dryRunOption = new Option<bool>(
            ["--dry-run"],
            description: "Show what would be done without executing");

        command.AddOption(configOption);
        command.AddOption(manifestOption);
        command.AddOption(appIdOption);
        command.AddOption(scopesOption);
        command.AddOption(verboseOption);
        command.AddOption(dryRunOption);

        command.SetHandler(async (config, manifest, appId, scopes, verbose, dryRun) =>
        {
            try
            {
                logger.LogInformation("Adding MCP server permissions to application...");
                logger.LogInformation("");

                // Check if config file exists or if --app-id was provided
                Agent365Config? setupConfig = null;
                if (File.Exists(config.FullName))
                {
                    // Load configuration if it exists
                    setupConfig = await configService.LoadAsync(config.FullName);
                }
                else if (string.IsNullOrWhiteSpace(appId))
                {
                    // Config doesn't exist and no --app-id provided
                    logger.LogError("Configuration file not found: {ConfigPath}", config.FullName);
                    logger.LogInformation("");
                    logger.LogInformation("To add MCP server permissions, you must either:");
                    logger.LogInformation("  1. Create a config file using: a365 config init");
                    logger.LogInformation("  2. Specify the application ID using: a365 develop addpermissions --app-id <your-app-id>");
                    logger.LogInformation("");
                    logger.LogInformation("Example: a365 develop addpermissions --app-id 12345678-1234-1234-1234-123456789abc --scopes McpServers.Mail.All");
                    Environment.Exit(1);
                    return;
                }

                // Determine target application ID
                string targetAppId;
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    targetAppId = appId;
                    logger.LogInformation("Target Application ID (from --app-id): {AppId}", targetAppId);
                }
                else if (setupConfig != null && !string.IsNullOrWhiteSpace(setupConfig.ClientAppId))
                {
                    targetAppId = setupConfig.ClientAppId;
                    logger.LogInformation("Target Application ID (from config): {AppId}", targetAppId);
                }
                else
                {
                    logger.LogError("No application ID specified. Use --app-id or ensure ClientAppId is set in config.");
                    logger.LogInformation("");
                    logger.LogInformation("Example: a365 develop addpermissions --app-id <your-app-id>");
                    Environment.Exit(1);
                    return;
                }

                // Determine manifest path
                var manifestPath = manifest?.FullName 
                    ?? Path.Combine(setupConfig?.DeploymentProjectPath ?? Environment.CurrentDirectory, "ToolingManifest.json");

                // Determine which scopes to add
                string[] requestedScopes;
                string[] uniqueAudiences;
                
                if (scopes != null && scopes.Length > 0)
                {
                    // User provided explicit scopes
                    requestedScopes = scopes;
                    logger.LogInformation("Using user-specified scopes: {Scopes}", string.Join(", ", requestedScopes));
                    logger.LogInformation("");
                    
                    // Try to read audiences from manifest if it exists
                    if (File.Exists(manifestPath))
                    {
                        // Use ManifestHelper to get audiences with fallback to mappings
                        uniqueAudiences = await ManifestHelper.GetRequiredAudiencesAsync(manifestPath);
                    }
                    else
                    {
                        // No manifest - need to derive audiences from scope names using ServerScopeMappings
                        logger.LogInformation("No manifest found. Attempting to derive audiences from scope names...");
                        
                        var derivedAudiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        
                        // Search through the scope mappings to find matching audiences
                        foreach (var scope in requestedScopes)
                        {
                            // Look for this scope in the ServerScopeMappings
                            var matchingMapping = McpConstants.ServerScopeMappings.ServerToScope
                                .FirstOrDefault(kvp => kvp.Value.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase));
                            
                            if (!string.IsNullOrEmpty(matchingMapping.Key) && !string.IsNullOrWhiteSpace(matchingMapping.Value.Audience))
                            {
                                derivedAudiences.Add(matchingMapping.Value.Audience);
                                logger.LogInformation("  Mapped scope '{Scope}' to audience '{Audience}'", scope, matchingMapping.Value.Audience);
                            }
                            else
                            {
                                logger.LogWarning("  Could not find audience mapping for scope: {Scope}", scope);
                            }
                        }
                        
                        uniqueAudiences = derivedAudiences.ToArray();
                    }
                }
                else
                {
                    // Read scopes and audiences from ToolingManifest.json
                    if (!File.Exists(manifestPath))
                    {
                        logger.LogError("ToolingManifest.json not found at: {Path}", manifestPath);
                        logger.LogInformation("");
                        logger.LogInformation("Please ensure ToolingManifest.json exists in your project directory");
                        logger.LogInformation("or specify scopes explicitly with --scopes option.");
                        logger.LogInformation("");
                        logger.LogInformation("Example: a365 develop addpermissions --scopes McpServers.Mail.All McpServers.Calendar.All");
                        Environment.Exit(1);
                        return;
                    }

                    logger.LogInformation("Reading MCP server configuration from: {Path}", manifestPath);

                    // Use ManifestHelper to extract scopes (includes fallback to mappings and McpServersMetadata.Read.All)
                    requestedScopes = await ManifestHelper.GetRequiredScopesAsync(manifestPath);

                    // Parse ToolingManifest.json to get audiences
                    var manifestJson = await File.ReadAllTextAsync(manifestPath);
                    var toolingManifest = JsonSerializer.Deserialize<ToolingManifest>(manifestJson);

                    if (toolingManifest?.McpServers == null || toolingManifest.McpServers.Length == 0)
                    {
                        logger.LogWarning("No MCP servers found in ToolingManifest.json");
                        logger.LogInformation("You can specify scopes explicitly with --scopes option.");
                        Environment.Exit(1);
                        return;
                    }

                    // Use ToolingManifest helper method to extract audiences
                    uniqueAudiences = toolingManifest.GetAllAudiences();

                    if (requestedScopes.Length == 0)
                    {
                        logger.LogError("No scopes found in ToolingManifest.json");
                        logger.LogInformation("You can specify scopes explicitly with --scopes option.");
                        Environment.Exit(1);
                        return;
                    }

                    logger.LogInformation("Collected {Count} unique scope(s) from manifest: {Scopes}", 
                        requestedScopes.Length, string.Join(", ", requestedScopes));
                }

                if (uniqueAudiences.Length == 0)
                {
                    logger.LogWarning("No audiences found in ToolingManifest.json. Cannot determine resource application IDs.");
                    logger.LogInformation("Note: Each MCP server should have an 'audience' field specifying the resource API.");
                    logger.LogInformation("");
                    logger.LogInformation("Using Agent 365 Tools resource as fallback...");
                    var environment = setupConfig?.Environment ?? "prod";
                    uniqueAudiences = new[] { ConfigConstants.GetAgent365ToolsResourceAppId(environment) };
                }

                logger.LogInformation("Found {Count} unique audience(s): {Audiences}", 
                    uniqueAudiences.Length, string.Join(", ", uniqueAudiences));
                logger.LogInformation("");

                // Dry run mode
                if (dryRun)
                {
                    logger.LogInformation("DRY RUN: Add MCP Server Permissions");
                    logger.LogInformation("Would add the following permissions to application {AppId}:", targetAppId);
                    logger.LogInformation("");
                    
                    foreach (var audience in uniqueAudiences)
                    {
                        logger.LogInformation("Resource: {Audience}", audience);
                        logger.LogInformation("  Scopes: {Scopes}", string.Join(", ", requestedScopes));
                    }
                    
                    logger.LogInformation("");
                    logger.LogInformation("No changes made (dry run mode)");
                    return;
                }

                // Add permissions for each unique audience
                logger.LogInformation("Adding permissions to application...");
                logger.LogInformation("");

                // Determine tenant ID (from config or detect from Azure CLI)
                string tenantId = await TenantDetectionHelper.DetectTenantIdAsync(setupConfig, logger) ?? string.Empty;

                int successCount = 0;
                int failureCount = 0;

                foreach (var audience in uniqueAudiences)
                {
                    logger.LogInformation("Processing audience: {Audience}", audience);
                    
                    try
                    {
                        var success = await graphApiService.AddRequiredResourceAccessAsync(
                            tenantId,
                            targetAppId,
                            audience,
                            requestedScopes,
                            isDelegated: true);

                        if (success)
                        {
                            logger.LogInformation("  [SUCCESS] Successfully added permissions for {Audience}", audience);
                            successCount++;
                        }
                        else
                        {
                            logger.LogError("  [FAILED] Failed to add permissions for {Audience}", audience);
                            failureCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("  [ERROR] Exception adding permissions for {Audience}: {Message}", audience, ex.Message);
                        if (verbose)
                        {
                            logger.LogError("    {StackTrace}", ex.StackTrace);
                        }
                        failureCount++;
                    }
                    
                    logger.LogInformation("");
                }

                // Summary
                logger.LogInformation("=== Summary ===");
                logger.LogInformation("Succeeded: {SuccessCount}/{Total}", successCount, uniqueAudiences.Length);
                logger.LogInformation("Failed: {FailureCount}/{Total}", failureCount, uniqueAudiences.Length);
                logger.LogInformation("");

                if (failureCount == 0)
                {
                    logger.LogInformation("[SUCCESS] All permissions added successfully!");
                    logger.LogInformation("");
                    logger.LogInformation("  Review permissions in Azure Portal: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/{AppId}", targetAppId);
                }
                else
                {
                    logger.LogWarning("Some permissions failed to add. Review the errors above.");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to add MCP server permissions: {Message}", ex.Message);
                Environment.Exit(1);
            }
        }, configOption, manifestOption, appIdOption, scopesOption, verboseOption, dryRunOption);

        return command;
    }
}
