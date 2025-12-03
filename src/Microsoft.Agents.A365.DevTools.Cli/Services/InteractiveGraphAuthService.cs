// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// Provides interactive authentication to Microsoft Graph using browser authentication.
/// This mimics the behavior of Connect-MgGraph in PowerShell which allows creating Agent Blueprints.
/// 
/// The key difference from Azure CLI authentication:
/// - Azure CLI tokens are delegated (user acting on behalf of themselves)
/// - This service gets application-level access through user consent
/// - Supports AgentApplication.Create application permission
/// 
/// PURE C# IMPLEMENTATION - NO POWERSHELL DEPENDENCIES
/// </summary>
public sealed class InteractiveGraphAuthService
{
    private readonly ILogger<InteractiveGraphAuthService> _logger;

    // Microsoft Graph PowerShell app ID (first-party Microsoft app with elevated privileges)
    private const string PowerShellAppId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

    // Scopes required for Agent Blueprint creation and inheritable permissions configuration
    private static readonly string[] RequiredScopes = new[]
    {
        "https://graph.microsoft.com/Application.ReadWrite.All",
        "https://graph.microsoft.com/AgentIdentityBlueprint.ReadWrite.All"
    };

    public InteractiveGraphAuthService(ILogger<InteractiveGraphAuthService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets an authenticated GraphServiceClient using interactive browser authentication.
    /// This uses the Microsoft Graph PowerShell app ID to get the same elevated privileges.
    /// </summary>
    public Task<GraphServiceClient> GetAuthenticatedGraphClientAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to authenticate to Microsoft Graph interactively...");
        _logger.LogInformation("This requires Application.ReadWrite.All and AgentIdentityBlueprint.ReadWrite.All permissions for Agent Blueprint operations.");
        _logger.LogInformation("");
        _logger.LogInformation("IMPORTANT: A browser window will open for authentication.");
        _logger.LogInformation("Please sign in with an account that has Global Administrator or similar privileges.");
        _logger.LogInformation("");

        try
        {
            // Try InteractiveBrowserCredential first (preferred method)
            try
            {
                var browserCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = PowerShellAppId,
                    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                    // MSAL will start local server on http://localhost:{random_port}
                    // This matches Microsoft Graph PowerShell app registration
                });
                
                _logger.LogInformation("Opening browser for authentication...");
                _logger.LogInformation("IMPORTANT: You must grant consent for the following permissions:");
                _logger.LogInformation("  - Application.ReadWrite.All (for creating applications and blueprints)");
                _logger.LogInformation("  - AgentIdentityBlueprint.ReadWrite.All (for configuring inheritable permissions)");
                _logger.LogInformation("");
                
                // Create GraphServiceClient with the credential
                var graphClient = new GraphServiceClient(browserCredential, RequiredScopes);

                _logger.LogInformation("Successfully authenticated to Microsoft Graph!");
                _logger.LogInformation("");
                
                return Task.FromResult(graphClient);
            }
            catch (Azure.Identity.AuthenticationFailedException ex) when (
                ex.Message.Contains("localhost") || 
                ex.Message.Contains("connection") ||
                ex.Message.Contains("redirect_uri"))
            {
                // Fallback to Device Code Flow if localhost redirect fails
                _logger.LogWarning("Browser authentication failed, falling back to device code flow...");
                _logger.LogInformation("");
                
                var deviceCodeCredential = new DeviceCodeCredential(new DeviceCodeCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = PowerShellAppId,
                    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                    DeviceCodeCallback = (code, cancellation) =>
                    {
                        _logger.LogInformation("");
                        _logger.LogInformation("=============================================================");
                        _logger.LogInformation("DEVICE CODE AUTHENTICATION");
                        _logger.LogInformation("=============================================================");
                        _logger.LogInformation("");
                        _logger.LogInformation("To sign in, use a web browser to open the page:");
                        _logger.LogInformation("    {0}", code.VerificationUri);
                        _logger.LogInformation("");
                        _logger.LogInformation("And enter the code:");
                        _logger.LogInformation("    {0}", code.UserCode);
                        _logger.LogInformation("");
                        _logger.LogInformation("=============================================================");
                        _logger.LogInformation("");
                        return Task.CompletedTask;
                    }
                });
                
                var graphClient = new GraphServiceClient(deviceCodeCredential, RequiredScopes);
                
                _logger.LogInformation("Successfully authenticated to Microsoft Graph!");
                _logger.LogInformation("");
                
                return Task.FromResult(graphClient);
            }
        }
        catch (Azure.Identity.AuthenticationFailedException ex) when (ex.Message.Contains("invalid_grant"))
        {
            _logger.LogError("Authentication failed: The user account doesn't have the required permissions.");
            _logger.LogError("Please ensure you are a Global Administrator or have Application.ReadWrite.All permission.");
            throw new InvalidOperationException(
                "Authentication failed: Insufficient permissions. " +
                "You must be a Global Administrator or have Application.ReadWrite.All permission.", ex);
        }
        catch (Azure.Identity.CredentialUnavailableException ex)
        {
            _logger.LogError("Interactive browser authentication is not available.");
            _logger.LogError("This may happen in non-interactive environments or when a browser is not available.");
            throw new InvalidOperationException(
                "Interactive authentication is not available. " +
                "Please ensure you're running this in an interactive environment with a browser.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate to Microsoft Graph: {Message}", ex.Message);
            _logger.LogError("");
            _logger.LogError("TROUBLESHOOTING:");
            _logger.LogError("  1. Ensure you are a Global Administrator or have Application.ReadWrite.All permission");
            _logger.LogError("  2. Make sure you're running in an interactive environment with a browser");
            _logger.LogError("  3. Check that the Microsoft Graph PowerShell app (14d82eec-204b-4c2f-b7e8-296a70dab67e) is available in your tenant");
            _logger.LogError("");
            throw new InvalidOperationException(
                $"Failed to authenticate to Microsoft Graph: {ex.Message}. " +
                "Please ensure you have the required permissions and are using a Global Administrator account.", ex);
        }
    }
}
