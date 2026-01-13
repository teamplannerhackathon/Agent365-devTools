// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

public class InteractiveGraphAuthServiceTests
{
    /// <summary>
    /// This test ensures that all required Graph API scopes are present in the RequiredScopes array.
    /// If any of these scopes are removed, the test will fail to prevent accidental permission reduction.
    /// 
    /// These scopes are critical for Agent Blueprint creation and inheritable permissions configuration:
    /// - Application.ReadWrite.All: Required for creating and managing app registrations
    /// - AgentIdentityBlueprint.ReadWrite.All: Required for Agent Blueprint operations
    /// - AgentIdentityBlueprint.UpdateAuthProperties.All: Required for updating blueprint auth properties
    /// - User.Read: Basic user profile access for authentication context
    /// </summary>
    [Fact]
    public void RequiredScopes_MustContainAllEssentialPermissions()
    {
        // Arrange
        var expectedScopes = new[]
        {
            "https://graph.microsoft.com/Application.ReadWrite.All",
            "https://graph.microsoft.com/AgentIdentityBlueprint.ReadWrite.All", 
            "https://graph.microsoft.com/AgentIdentityBlueprint.UpdateAuthProperties.All",
            "https://graph.microsoft.com/User.Read"
        };

        var logger = Substitute.For<ILogger<InteractiveGraphAuthService>>();
        var service = new InteractiveGraphAuthService(logger, "12345678-1234-1234-1234-123456789abc");

        // Act - Use reflection to access the private static RequiredScopes field
        var requiredScopesField = typeof(InteractiveGraphAuthService)
            .GetField("RequiredScopes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(requiredScopesField);
        var actualScopes = (string[])requiredScopesField.GetValue(null)!;

        // Assert
        Assert.NotNull(actualScopes);
        Assert.Equal(expectedScopes.Length, actualScopes.Length);
        
        foreach (var expectedScope in expectedScopes)
        {
            Assert.Contains(expectedScope, actualScopes);
        }
    }

    [Fact]
    public void Constructor_WithValidGuidClientAppId_ShouldSucceed()
    {
        // Arrange
        var logger = Substitute.For<ILogger<InteractiveGraphAuthService>>();
        var validGuid = "12345678-1234-1234-1234-123456789abc";

        // Act & Assert - Should not throw
        var service = new InteractiveGraphAuthService(logger, validGuid);
        Assert.NotNull(service);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNullOrEmptyClientAppId_ShouldThrowArgumentNullException(string? clientAppId)
    {
        // Arrange
        var logger = Substitute.For<ILogger<InteractiveGraphAuthService>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InteractiveGraphAuthService(logger, clientAppId!));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("invalid-format")]
    public void Constructor_WithInvalidGuidClientAppId_ShouldThrowArgumentException(string clientAppId)
    {
        // Arrange
        var logger = Substitute.For<ILogger<InteractiveGraphAuthService>>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new InteractiveGraphAuthService(logger, clientAppId));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var validGuid = "12345678-1234-1234-1234-123456789abc";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InteractiveGraphAuthService(null!, validGuid));
    }

    #region WAM Configuration Tests (GitHub Issues #146 and #151)

    /// <summary>
    /// Documents the expected behavior for WAM (Windows Authentication Broker) configuration.
    /// 
    /// GitHub Issue #146: Users receive AADSTS50011 error because WAM uses the broker redirect URI
    /// (ms-appx-web://Microsoft.AAD.BrokerPlugin/{guid}) which is not configured in app registration.
    /// 
    /// GitHub Issue #151: Users receive "A window handle must be configured" error because WAM
    /// requires a parent window handle which console applications don't provide.
    /// 
    /// The fix is to use MSAL directly with .WithUseEmbeddedWebView(false) via the MsalBrowserCredential
    /// class to force the system browser flow and bypass WAM entirely.
    /// 
    /// This test cannot verify the actual credential options since they are created inside private
    /// methods, but it documents the expected behavior and can be manually verified by:
    /// 1. Running `a365 setup all` on Windows 10/11
    /// 2. Confirming the system browser opens (not an embedded webview)
    /// 3. Confirming no "window handle" or redirect URI mismatch errors occur
    /// </summary>
    [Fact]
    [Trait("Category", "Documentation")]
    public void MsalBrowserCredential_ShouldBeConfiguredToDisableWAM()
    {
        // This test documents that MsalBrowserCredential uses MSAL's PublicClientApplicationBuilder
        // with .WithUseEmbeddedWebView(false) which:
        //
        // 1. Forces the system browser to be used instead of WAM
        // 2. Avoids the "window handle" error (Issue #151)
        // 3. Uses the http://localhost:8400/ redirect URI instead of broker URI (Issue #146)
        // 4. Works consistently across Windows 10, Windows 11, and non-Windows platforms
        // 5. Uses the non-deprecated MSAL API instead of Azure.Identity's obsolete BrowserCustomizationOptions
        //
        // See: https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam
        
        // Note: MsalBrowserCredential is used in:
        // - InteractiveGraphAuthService.GetAuthenticatedGraphClientAsync()
        // - AuthenticationService.GetAccessTokenAsync()
        // - BlueprintSubcommand.GetTokenFromGraphClient()
        
        Assert.True(true, "WAM configuration is documented. Manual verification required.");
    }

    /// <summary>
    /// Verifies that MsalBrowserCredential can be constructed with valid parameters.
    /// </summary>
    [Fact]
    public void MsalBrowserCredential_WithValidParameters_ShouldConstruct()
    {
        // Arrange
        var clientId = "12345678-1234-1234-1234-123456789abc";
        var tenantId = "87654321-4321-4321-4321-cba987654321";
        var redirectUri = "http://localhost:8400";

        // Act
        var credential = new MsalBrowserCredential(clientId, tenantId, redirectUri);

        // Assert
        Assert.NotNull(credential);
    }

    /// <summary>
    /// Verifies that MsalBrowserCredential throws on null client ID.
    /// </summary>
    [Fact]
    public void MsalBrowserCredential_WithNullClientId_ShouldThrow()
    {
        // Arrange
        var tenantId = "87654321-4321-4321-4321-cba987654321";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MsalBrowserCredential(null!, tenantId));
    }

    /// <summary>
    /// Verifies that MsalBrowserCredential throws on null tenant ID.
    /// </summary>
    [Fact]
    public void MsalBrowserCredential_WithNullTenantId_ShouldThrow()
    {
        // Arrange
        var clientId = "12345678-1234-1234-1234-123456789abc";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MsalBrowserCredential(clientId, null!));
    }

    /// <summary>
    /// Integration test for WAM configuration - can be run manually to verify the fix.
    /// This test is skipped by default in automated runs as it requires user interaction.
    /// 
    /// To run manually: dotnet test --filter "Category=Integration"
    /// </summary>
    [Fact(Skip = "Integration test requires manual verification on Windows 10/11")]
    [Trait("Category", "Integration")]
    public void MsalBrowserCredential_ManualTest_ShouldOpenSystemBrowser()
    {
        // This test is marked as Integration and should be skipped in CI/CD pipelines.
        // To verify the WAM fix works:
        //
        // 1. Run this command on Windows 10/11:
        //    a365 setup all
        //
        // 2. Expected behavior:
        //    - System default browser opens (Chrome, Edge, Firefox, etc.)
        //    - NOT an embedded webview window
        //    - Redirect uses http://localhost:8400/
        //    - No "window handle" error
        //    - No AADSTS50011 redirect URI mismatch error
        //
        // 3. The fix uses MSAL directly with:
        //    PublicClientApplicationBuilder.Create(clientId)
        //        .WithAuthority(...)
        //        .WithRedirectUri(...)
        //        .Build()
        //        .AcquireTokenInteractive(scopes)
        //        .WithUseEmbeddedWebView(false)  // <-- Key setting
        //        .ExecuteAsync()
        
        Assert.True(true, "Manual verification required");
    }

    #endregion
}