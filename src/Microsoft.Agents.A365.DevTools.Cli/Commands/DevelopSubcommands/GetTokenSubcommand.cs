// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Helpers;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

namespace Microsoft.Agents.A365.DevTools.Cli.Commands.DevelopSubcommands;

/// <summary>
/// GetToken subcommand - Retrieves bearer tokens for MCP server authentication
/// </summary>
internal static class GetTokenSubcommand
{
    public static Command CreateCommand(
        ILogger logger,
        IConfigService configService,
        AuthenticationService authService)
    {
        var command = new Command(
            "gettoken",
            "Retrieve bearer tokens for MCP server authentication\n" +
            "Scopes are read from ToolingManifest.json or specified via command line");

        var configOption = new Option<FileInfo>(
            ["--config", "-c"],
            getDefaultValue: () => new FileInfo("a365.config.json"),
            description: "Configuration file path");

        var appIdOption = new Option<string?>(
            ["--app-id"],
            description: "Application (client) ID to get token for. If not specified, uses the client app ID from config")
        {
            IsRequired = false
        };

        var manifestOption = new Option<FileInfo?>(
            ["--manifest", "-m"],
            description: "Path to ToolingManifest.json (defaults to current directory)");

        var scopesOption = new Option<string[]?>(
            ["--scopes"],
            description: "Specific scopes to request (e.g., McpServers.Mail.All McpServers.Calendar.All). If not specified, uses all scopes from ToolingManifest.json")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var outputFormatOption = new Option<string>(
            ["--output", "-o"],
            getDefaultValue: () => "table",
            description: "Output format: table, json, or raw");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            description: "Show detailed output including full token");

        var forceRefreshOption = new Option<bool>(
            ["--force-refresh"],
            description: "Force token refresh even if cached token is valid");

        var setEnvOption = new Option<bool>(
            ["--set-env"],
            description: "Set the token as the BEARER_TOKEN environment variable");

        var clearEnvOption = new Option<bool>(
            ["--clear-env"],
            description: "Clear the BEARER_TOKEN environment variable");

        command.AddOption(configOption);
        command.AddOption(appIdOption);
        command.AddOption(manifestOption);
        command.AddOption(scopesOption);
        command.AddOption(outputFormatOption);
        command.AddOption(verboseOption);
        command.AddOption(forceRefreshOption);
        command.AddOption(setEnvOption);
        command.AddOption(clearEnvOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var config = context.ParseResult.GetValueForOption(configOption)!;
            var appId = context.ParseResult.GetValueForOption(appIdOption);
            var manifest = context.ParseResult.GetValueForOption(manifestOption);
            var scopes = context.ParseResult.GetValueForOption(scopesOption);
            var outputFormat = context.ParseResult.GetValueForOption(outputFormatOption)!;
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var forceRefresh = context.ParseResult.GetValueForOption(forceRefreshOption);
            var setEnv = context.ParseResult.GetValueForOption(setEnvOption);
            var clearEnv = context.ParseResult.GetValueForOption(clearEnvOption);

            try
            {
                // Handle clear environment variable option
                if (clearEnv)
                {
                    HandleClearEnvironmentVariable(logger);
                    return;
                }

                logger.LogInformation("Retrieving bearer token for MCP servers...");
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
                    logger.LogInformation("To retrieve bearer tokens, you must either:");
                    logger.LogInformation("  1. Create a config file using: a365 config init");
                    logger.LogInformation("  2. Specify the application ID using: a365 develop gettoken --app-id <your-app-id>");
                    logger.LogInformation("");
                    logger.LogInformation("Example: a365 develop gettoken --app-id 12345678-1234-1234-1234-123456789abc --scopes McpServers.Mail.All");
                    Environment.Exit(1);
                    return;
                }

                // Determine manifest path
                var manifestPath = manifest?.FullName 
                    ?? Path.Combine(setupConfig?.DeploymentProjectPath ?? Environment.CurrentDirectory, "ToolingManifest.json");

                // Determine which scopes to request
                string[] requestedScopes;
                
                if (scopes != null && scopes.Length > 0)
                {
                    // User provided explicit scopes
                    requestedScopes = scopes;
                    logger.LogInformation("Using user-specified scopes: {Scopes}", string.Join(", ", requestedScopes));
                }
                else
                {
                    // Read scopes from ToolingManifest.json
                    if (!File.Exists(manifestPath))
                    {
                        logger.LogError("ToolingManifest.json not found at: {Path}", manifestPath);
                        logger.LogInformation("");
                        logger.LogInformation("Please ensure ToolingManifest.json exists in your project directory");
                        logger.LogInformation("or specify scopes explicitly with --scopes option.");
                        logger.LogInformation("");
                        logger.LogInformation("Example: a365 develop gettoken --scopes McpServers.Mail.All McpServers.Calendar.All");
                        Environment.Exit(1);
                        return;
                    }

                    logger.LogInformation("Reading MCP server configuration from: {Path}", manifestPath);

                    // Use ManifestHelper to extract scopes (includes fallback to mappings and McpServersMetadata.Read.All)
                    requestedScopes = await ManifestHelper.GetRequiredScopesAsync(manifestPath);

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

                logger.LogInformation("");

                // Get the Agent 365 Tools resource App ID for the environment
                var environment = setupConfig?.Environment ?? "prod";
                var resourceAppId = ConfigConstants.GetAgent365ToolsResourceAppId(environment);
                logger.LogInformation("Agent 365 Tools Resource App ID: {AppId}", resourceAppId);
                logger.LogInformation("Requesting scopes: {Scopes}", string.Join(", ", requestedScopes));
                logger.LogInformation("");

                // Acquire token with explicit scopes
                logger.LogInformation("Acquiring access token with explicit scopes...");
                
                // Determine tenant ID (from config or detect from Azure CLI)
                string? tenantId = await TenantDetectionHelper.DetectTenantIdAsync(setupConfig, logger);
                
                try
                {
                    // Determine which client app to use for authentication
                    string? clientAppId = null;
                    if (!string.IsNullOrWhiteSpace(appId))
                    {
                        // User specified --app-id: use it as the client (caller) application
                        clientAppId = appId;
                        logger.LogInformation("Using custom client application: {ClientAppId}", clientAppId);
                    }
                    else if (setupConfig != null && !string.IsNullOrWhiteSpace(setupConfig.ClientAppId))
                    {
                        // Use client app from config
                        clientAppId = setupConfig.ClientAppId;
                        logger.LogInformation("Using client application from config: {ClientAppId}", clientAppId);
                    }
                    else
                    {
                        throw new InvalidOperationException("No client application ID specified. Use --app-id or ensure ClientAppId is set in config.");
                    }
                    
                    logger.LogInformation("");
                    
                    // Use GetAccessTokenWithScopesAsync for explicit scope control
                    var token = await authService.GetAccessTokenWithScopesAsync(
                        resourceAppId,
                        requestedScopes,
                        tenantId,
                        forceRefresh,
                        clientAppId,
                        useInteractiveBrowser: true);

                    if (string.IsNullOrWhiteSpace(token))
                    {
                        logger.LogError("Failed to acquire token");
                        Environment.Exit(1);
                        return;
                    }

                    logger.LogInformation("[SUCCESS] Token acquired successfully with scopes: {Scopes}", 
                        string.Join(", ", requestedScopes));
                    logger.LogInformation("");

                    // Get token cache file path for display (matches AuthenticationService cache location)
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var tokenCachePath = Path.Combine(appDataPath, 
                        AuthenticationConstants.ApplicationName, 
                        AuthenticationConstants.TokenCacheFileName);

                    // Create a single result representing the consolidated token
                    var tokenResult = new McpServerTokenResult
                    {
                        ServerName = "Agent 365 Tools (All MCP Servers)",
                        Url = ConfigConstants.GetDiscoverEndpointUrl(environment),
                        Scope = string.Join(", ", requestedScopes),
                        Audience = resourceAppId,
                        Success = true,
                        Token = token,
                        ExpiresOn = DateTime.UtcNow.AddHours(1), // Estimate
                        CacheFilePath = tokenCachePath
                    };

                    var tokenResults = new List<McpServerTokenResult> { tokenResult };

                    // Display results based on output format
                    DisplayResults(tokenResults, outputFormat, verbose, logger);

                    // Set environment variable if requested
                    if (setEnv)
                    {
                        HandleSetEnvironmentVariable(token, logger);
                    }

                    logger.LogInformation("Token acquired successfully!");
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to acquire token: {Message}", ex.Message);
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve bearer token: {Message}", ex.Message);
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static void DisplayResults(
        List<McpServerTokenResult> results,
        string outputFormat,
        bool verbose,
        ILogger logger)
    {
        switch (outputFormat.ToLowerInvariant())
        {
            case "json":
                DisplayJsonResults(results, verbose);
                break;
            case "raw":
                DisplayRawResults(results, verbose);
                break;
            case "table":
            default:
                DisplayTableResults(results, verbose, logger);
                break;
        }
    }

    private static void DisplayTableResults(
        List<McpServerTokenResult> results,
        bool verbose,
        ILogger logger)
    {
        logger.LogInformation("=== MCP Server Bearer Tokens ===");
        logger.LogInformation("");

        foreach (var result in results)
        {
            logger.LogInformation("Server: {Name}", result.ServerName);
            logger.LogInformation("  URL: {Url}", result.Url ?? "(not specified)");
            logger.LogInformation("  Scope: {Scope}", result.Scope ?? "(not specified)");
            logger.LogInformation("  Audience: {Audience}", result.Audience ?? "(not specified)");

            if (result.Success)
            {
                logger.LogInformation("  Status: [SUCCESS]");
                logger.LogInformation("  Expires: ~{Expiry}", result.ExpiresOn?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown");

                if (!string.IsNullOrWhiteSpace(result.Token))
                {
                    logger.LogInformation("  Token: {Token}", result.Token);
                }

                if (!string.IsNullOrWhiteSpace(result.CacheFilePath))
                {
                    logger.LogInformation("  Cached at: {CacheFilePath}", result.CacheFilePath);
                }
            }
            else
            {
                logger.LogInformation("  Status: [FAILED]");
                logger.LogInformation("  Error: {Error}", result.Error ?? "Unknown error");
            }

            logger.LogInformation("");
        }
    }

    private static void DisplayJsonResults(List<McpServerTokenResult> results, bool verbose)
    {
        var output = results.Select(r => new
        {
            serverName = r.ServerName,
            url = r.Url,
            scope = r.Scope,
            audience = r.Audience,
            success = r.Success,
            token = r.Token,
            expiresOn = r.ExpiresOn?.ToString("o"),
            error = r.Error,
            cacheFilePath = r.CacheFilePath
        });

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Console.WriteLine(json);
    }

    private static void DisplayRawResults(List<McpServerTokenResult> results, bool verbose)
    {
        foreach (var result in results)
        {
            if (result.Success && !string.IsNullOrWhiteSpace(result.Token))
            {
                if (verbose)
                {
                    Console.WriteLine($"# {result.ServerName}");
                    Console.WriteLine($"# Scope: {result.Scope}");
                    Console.WriteLine($"# Audience: {result.Audience}");
                }
                Console.WriteLine(result.Token);
                if (verbose)
                {
                    Console.WriteLine();
                }
            }
        }
    }

    /// <summary>
    /// Handles the --clear-env option to clear the BEARER_TOKEN environment variable
    /// </summary>
    /// <param name="logger">Logger instance for output</param>
    private static void HandleClearEnvironmentVariable(ILogger logger)
    {
        var envVarName = AuthenticationConstants.MCPBearerTokenEnvironmentVariable;
        var currentValue = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(envVarName, null, EnvironmentVariableTarget.Process);
        
        logger.LogInformation("=== Clear Environment Variable ===");
        logger.LogInformation("Variable Name: {EnvVar}", envVarName);
        logger.LogInformation("Status: Cleared from current process");
        
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            logger.LogInformation("Previous Value: {TokenPreview}...", currentValue.Length > 20 ? currentValue[..20] : currentValue);
        }
        
        logger.LogInformation("");
        logger.LogInformation("Note: The environment variable is cleared only in the current process.");
        logger.LogInformation("To clear it in your shell session, use one of these commands:");
        logger.LogInformation("");
        
        // Detect OS and provide appropriate command
        if (OperatingSystem.IsWindows())
        {
            logger.LogInformation("PowerShell:");
            logger.LogInformation("  Remove-Item Env:{EnvVar}", envVarName);
            logger.LogInformation("");
            logger.LogInformation("Command Prompt:");
            logger.LogInformation("  set {EnvVar}=", envVarName);
        }
        else
        {
            logger.LogInformation("Bash/Zsh:");
            logger.LogInformation("  unset {EnvVar}", envVarName);
        }
        logger.LogInformation("");
        logger.LogInformation("Environment variable cleared successfully!");
    }

    /// <summary>
    /// Handles the --set-env option to set the BEARER_TOKEN environment variable
    /// </summary>
    /// <param name="token">The bearer token to set</param>
    /// <param name="logger">Logger instance for output</param>
    private static void HandleSetEnvironmentVariable(string token, ILogger logger)
    {
        var envVarName = AuthenticationConstants.MCPBearerTokenEnvironmentVariable;
        Environment.SetEnvironmentVariable(envVarName, token, EnvironmentVariableTarget.Process);
        logger.LogInformation("");
        logger.LogInformation("=== Environment Variable ===");
        logger.LogInformation("Variable Name: {EnvVar}", envVarName);
        logger.LogInformation("Value: {TokenPreview}... (token set)", token.Length > 20 ? token[..20] : token);
        logger.LogInformation("");
        logger.LogInformation("Note: The environment variable is set only in the current process.");
        logger.LogInformation("To set it in your shell session, use one of these commands:");
        logger.LogInformation("");
        
        // Detect OS and provide appropriate command
        if (OperatingSystem.IsWindows())
        {
            logger.LogInformation("PowerShell:");
            logger.LogInformation("  $env:{EnvVar} = (a365 develop gettoken --output raw)", envVarName);
            logger.LogInformation("");
            logger.LogInformation("Command Prompt:");
            logger.LogInformation("  for /f %i in ('a365 develop gettoken --output raw') do set {EnvVar}=%i", envVarName);
            logger.LogInformation("");
            logger.LogInformation("To clear the variable:");
            logger.LogInformation("  PowerShell: Remove-Item Env:{EnvVar}", envVarName);
            logger.LogInformation("  Command Prompt: set {EnvVar}=", envVarName);
        }
        else
        {
            logger.LogInformation("Bash/Zsh:");
            logger.LogInformation("  export {EnvVar}=$(a365 develop gettoken --output raw)", envVarName);
            logger.LogInformation("");
            logger.LogInformation("To clear the variable:");
            logger.LogInformation("  unset {EnvVar}", envVarName);
        }
        logger.LogInformation("");
    }

    private class McpServerTokenResult
    {
        public string ServerName { get; set; } = string.Empty;
        public string? Url { get; set; }
        public string? Scope { get; set; }
        public string? Audience { get; set; }
        public bool Success { get; set; }
        public string? Token { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public string? Error { get; set; }
        public string? CacheFilePath { get; set; }
    }
}
