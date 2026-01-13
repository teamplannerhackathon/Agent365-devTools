// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// A custom TokenCredential that uses MSAL directly for interactive browser authentication.
/// This provides better control over the authentication flow and avoids Windows Authentication
/// Broker (WAM) issues that can occur with Azure.Identity's InteractiveBrowserCredential.
/// 
/// Uses PublicClientApplicationBuilder with .WithUseEmbeddedWebView(false) to force
/// the system browser, which is the recommended approach for console applications.
/// 
/// See: https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam
/// Fixes GitHub issues #146 and #151.
/// </summary>
public sealed class MsalBrowserCredential : TokenCredential
{
    private readonly IPublicClientApplication _publicClientApp;
    private readonly ILogger? _logger;
    private readonly string _tenantId;

    /// <summary>
    /// Creates a new instance of MsalBrowserCredential.
    /// </summary>
    /// <param name="clientId">The application (client) ID.</param>
    /// <param name="tenantId">The directory (tenant) ID.</param>
    /// <param name="redirectUri">The redirect URI for authentication callbacks.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public MsalBrowserCredential(
        string clientId,
        string tenantId,
        string? redirectUri = null,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        _tenantId = tenantId;
        _logger = logger;

        var effectiveRedirectUri = redirectUri ?? AuthenticationConstants.LocalhostRedirectUri;

        _publicClientApp = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri(effectiveRedirectUri)
            .Build();
    }

    /// <inheritdoc/>
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var scopes = requestContext.Scopes;

        try
        {
            // First, try to acquire token silently from cache
            var accounts = await _publicClientApp.GetAccountsAsync();
            var account = accounts.FirstOrDefault();

            if (account != null)
            {
                try
                {
                    _logger?.LogDebug("Attempting to acquire token silently from cache...");
                    var silentResult = await _publicClientApp
                        .AcquireTokenSilent(scopes, account)
                        .ExecuteAsync(cancellationToken);

                    _logger?.LogDebug("Successfully acquired token from cache.");
                    return new AccessToken(silentResult.AccessToken, silentResult.ExpiresOn);
                }
                catch (MsalUiRequiredException)
                {
                    _logger?.LogDebug("Token cache miss or expired, interactive authentication required.");
                }
            }

            // Acquire token interactively using system browser (not WAM)
            _logger?.LogInformation("Opening browser for authentication...");

            var interactiveResult = await _publicClientApp
                .AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false) // Force system browser, avoid WAM issues
                .ExecuteAsync(cancellationToken);

            _logger?.LogDebug("Successfully acquired token via interactive browser authentication.");
            return new AccessToken(interactiveResult.AccessToken, interactiveResult.ExpiresOn);
        }
        catch (MsalException ex)
        {
            _logger?.LogError(ex, "MSAL authentication failed: {Message}", ex.Message);
            throw new AuthenticationFailedException($"Failed to acquire token: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Exception thrown when authentication fails.
/// </summary>
public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message) { }
    public AuthenticationFailedException(string message, Exception innerException) : base(message, innerException) { }
}
