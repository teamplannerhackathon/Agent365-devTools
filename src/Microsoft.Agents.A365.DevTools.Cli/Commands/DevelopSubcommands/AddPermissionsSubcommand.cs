// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;
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
                HashSet<string> uniqueAudiences = new();
                
                if (scopes != null && scopes.Length > 0)
                {
                    // User provided explicit scopes
                    requestedScopes = scopes;
                    logger.LogInformation("Using user-specified scopes: {Scopes}", string.Join(", ", requestedScopes));
                    logger.LogInformation("");
                    
                    // For explicit scopes, we still need to read audiences from manifest
                    if (File.Exists(manifestPath))
                    {
                        var manifestJson = await File.ReadAllTextAsync(manifestPath);
                        var toolingManifest = JsonSerializer.Deserialize<ToolingManifest>(manifestJson);
                        
                        if (toolingManifest?.McpServers != null && toolingManifest.McpServers.Length > 0)
                        {
                            foreach (var server in toolingManifest.McpServers)
                            {
                                if (!string.IsNullOrWhiteSpace(server.Audience))
                                {
                                    uniqueAudiences.Add(server.Audience);
                                }
                            }
                        }
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

                    // Parse ToolingManifest.json
                    var manifestJson = await File.ReadAllTextAsync(manifestPath);
                    var toolingManifest = JsonSerializer.Deserialize<ToolingManifest>(manifestJson);

                    if (toolingManifest?.McpServers == null || toolingManifest.McpServers.Length == 0)
                    {
                        logger.LogWarning("No MCP servers found in ToolingManifest.json");
                        logger.LogInformation("You can specify scopes explicitly with --scopes option.");
                        Environment.Exit(1);
                        return;
                    }

                    // Collect all unique scopes and audiences from manifest
                    var scopeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var server in toolingManifest.McpServers)
                    {
                        if (!string.IsNullOrWhiteSpace(server.Scope))
                        {
                            scopeSet.Add(server.Scope);
                        }
                        
                        if (!string.IsNullOrWhiteSpace(server.Audience))
                        {
                            uniqueAudiences.Add(server.Audience);
                        }
                    }

                    if (scopeSet.Count == 0)
                    {
                        logger.LogError("No scopes found in ToolingManifest.json");
                        logger.LogInformation("You can specify scopes explicitly with --scopes option.");
                        Environment.Exit(1);
                        return;
                    }

                    requestedScopes = scopeSet.ToArray();
                    logger.LogInformation("Collected {Count} unique scope(s) from manifest: {Scopes}", 
                        requestedScopes.Length, string.Join(", ", requestedScopes));
                }

                if (uniqueAudiences.Count == 0)
                {
                    logger.LogWarning("No audiences found in ToolingManifest.json. Cannot determine resource application IDs.");
                    logger.LogInformation("Note: Each MCP server should have an 'audience' field specifying the resource API.");
                    logger.LogInformation("");
                    logger.LogInformation("Using Agent 365 Tools resource as fallback...");
                    var environment = setupConfig?.Environment ?? "prod";
                    uniqueAudiences.Add(ConfigConstants.GetAgent365ToolsResourceAppId(environment));
                }

                logger.LogInformation("Found {Count} unique audience(s): {Audiences}", 
                    uniqueAudiences.Count, string.Join(", ", uniqueAudiences));
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
                string tenantId = string.Empty;
                if (setupConfig != null && !string.IsNullOrWhiteSpace(setupConfig.TenantId))
                {
                    tenantId = setupConfig.TenantId;
                }
                else
                {
                    // When config is not available or tenant ID is missing, try to detect from Azure CLI
                    logger.LogInformation("No tenant ID in config. Attempting to detect from Azure CLI context...");
                    
                    try
                    {
                        var executor = new CommandExecutor(
                            Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandExecutor>.Instance);
                        
                        var result = await executor.ExecuteAsync(
                            "az",
                            "account show --query tenantId -o tsv",
                            captureOutput: true,
                            suppressErrorLogging: true);

                        if (result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput))
                        {
                            tenantId = result.StandardOutput.Trim();
                            logger.LogInformation("Detected tenant ID from Azure CLI: {TenantId}", tenantId);
                        }
                        else
                        {
                            logger.LogWarning("Could not detect tenant ID from Azure CLI.");
                            logger.LogWarning("You may need to run 'az login' first.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Failed to detect tenant ID from Azure CLI: {Message}", ex.Message);
                    }
                    
                    if (string.IsNullOrWhiteSpace(tenantId))
                    {
                        logger.LogInformation("");
                        logger.LogInformation("For best results, either:");
                        logger.LogInformation("  1. Run 'az login' to set Azure CLI context");
                        logger.LogInformation("  2. Create a config file with: a365 config init");
                        logger.LogInformation("");
                        
                        // Use empty string as fallback
                        tenantId = string.Empty;
                    }
                }

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
                logger.LogInformation("Succeeded: {SuccessCount}/{Total}", successCount, uniqueAudiences.Count);
                logger.LogInformation("Failed: {FailureCount}/{Total}", failureCount, uniqueAudiences.Count);
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
