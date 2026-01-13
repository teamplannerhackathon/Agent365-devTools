// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using System.Runtime.InteropServices;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// A custom TokenCredential that uses MSAL directly for interactive authentication.
/// On Windows, this uses WAM (Windows Authentication Broker) for a native sign-in experience
/// that doesn't require opening a browser. On other platforms, it falls back to system browser.
/// 
/// See: https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam
/// Fixes GitHub issues #146 and #151.
/// </summary>
public sealed class MsalBrowserCredential : TokenCredential
{
    private readonly IPublicClientApplication _publicClientApp;
    private readonly ILogger? _logger;
    private readonly string _tenantId;
    private readonly bool _useWam;
    private readonly IntPtr _windowHandle;

    // P/Invoke to get window handles for WAM on Windows
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Creates a new instance of MsalBrowserCredential.
    /// </summary>
    /// <param name="clientId">The application (client) ID.</param>
    /// <param name="tenantId">The directory (tenant) ID.</param>
    /// <param name="redirectUri">The redirect URI for authentication callbacks.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <param name="useWam">Whether to use WAM on Windows. Default is true.</param>
    public MsalBrowserCredential(
        string clientId,
        string tenantId,
        string? redirectUri = null,
        ILogger? logger = null,
        bool useWam = true)
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
        
        // Get window handle for WAM on Windows
        // Try multiple sources: console window, foreground window, or desktop window
        _windowHandle = IntPtr.Zero;
        _useWam = useWam && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        if (_useWam)
        {
            try
            {
                // Try console window first (works for cmd.exe, PowerShell)
                _windowHandle = GetConsoleWindow();
                
                // If no console window, try foreground window (works for Windows Terminal)
                if (_windowHandle == IntPtr.Zero)
                {
                    _windowHandle = GetForegroundWindow();
                }
                
                // Last resort: use desktop window (always valid)
                if (_windowHandle == IntPtr.Zero)
                {
                    _windowHandle = GetDesktopWindow();
                }
                
                _logger?.LogDebug("Window handle for WAM: {Handle}", _windowHandle);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get window handle, falling back to system browser");
                _useWam = false;
            }
        }

        var builder = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId);

        if (_useWam)
        {
            // Use WAM broker on Windows for native authentication experience
            // WAM provides SSO with Windows accounts and doesn't require browser
            _logger?.LogDebug("Configuring WAM broker for Windows authentication");
            
            var brokerOptions = new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
            {
                Title = "Agent365 Tools Authentication"
            };
            
            builder = builder
                .WithBroker(brokerOptions)
                .WithParentActivityOrWindow(() => _windowHandle)
                .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{clientId}");
        }
        else
        {
            // Use system browser on non-Windows platforms or when WAM isn't available
            _logger?.LogDebug("Using system browser for authentication");
            var effectiveRedirectUri = redirectUri ?? AuthenticationConstants.LocalhostRedirectUri;
            builder = builder.WithRedirectUri(effectiveRedirectUri);
        }

        _publicClientApp = builder.Build();
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

            // Acquire token interactively
            AuthenticationResult interactiveResult;
            
            if (_useWam)
            {
                // WAM on Windows - native authentication dialog, no browser needed
                _logger?.LogInformation("Authenticating via Windows Account Manager...");
                interactiveResult = await _publicClientApp
                    .AcquireTokenInteractive(scopes)
                    .ExecuteAsync(cancellationToken);
            }
            else
            {
                // System browser on Mac/Linux
                _logger?.LogInformation("Opening browser for authentication...");
                interactiveResult = await _publicClientApp
                    .AcquireTokenInteractive(scopes)
                    .WithUseEmbeddedWebView(false)
                    .ExecuteAsync(cancellationToken);
            }

            _logger?.LogDebug("Successfully acquired token via interactive authentication.");
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
