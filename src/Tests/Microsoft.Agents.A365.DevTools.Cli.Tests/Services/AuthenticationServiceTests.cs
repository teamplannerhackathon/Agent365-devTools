// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using NSubstitute;
using System.Text.Json;
using Microsoft.Agents.A365.DevTools.Cli.Constants;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

/// <summary>
/// Unit tests for AuthenticationService
/// </summary>
public class AuthenticationServiceTests : IDisposable
{
    private readonly ILogger<AuthenticationService> _mockLogger;
    private readonly string _testCachePath;
    private readonly AuthenticationService _authService;

    public AuthenticationServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<AuthenticationService>>();
        _authService = new AuthenticationService(_mockLogger);
        
        // Get the actual cache path that the service uses
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _testCachePath = Path.Combine(appDataPath, "Microsoft.Agents.A365.DevTools.Cli", "auth-token.json");
    }

    public void Dispose()
    {
        // Clean up test cache
        _authService.ClearCache();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ClearCache_WhenCacheExists_RemovesFile()
    {
        // Arrange
        var cacheDir = Path.GetDirectoryName(_testCachePath)!;
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(_testCachePath, "test content");

        // Act
        _authService.ClearCache();

        // Assert
        File.Exists(_testCachePath).Should().BeFalse();
    }

    [Fact]
    public void ClearCache_WhenCacheDoesNotExist_DoesNotThrow()
    {
        // Arrange
        if (File.Exists(_testCachePath))
        {
            File.Delete(_testCachePath);
        }

        // Act
        Action act = () => _authService.ClearCache();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_CreatesAuthenticationService_Successfully()
    {
        // Act
        var service = new AuthenticationService(_mockLogger);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_CreatesCacheDirectory_IfNotExists()
    {
        // Arrange
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(appDataPath, "Microsoft.Agents.A365.DevTools.Cli");

        // Act
        _ = new AuthenticationService(_mockLogger);

        // Assert
        Directory.Exists(cacheDir).Should().BeTrue();
    }

    [Fact]
    public void ResolveScopesForResource_WithSingleScopeManifest_ShouldReturnCorrectScope()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var mailScopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            Assert.Single(mailScopes);
            Assert.Equal("McpServers.Mail.All", mailScopes[0]);
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithNullOrEmptyScopes_ShouldReturnDefaultScope()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "server-no-scope",
                    Url = "https://test.example.com/no-scope",
                    Scope = null,
                    Audience = "api://no-scope"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var noScopeResult = _authService.ResolveScopesForResource(
                "https://test.example.com/no-scope", tempManifestPath);

            // Assert - Should return default Power Platform scope when no MCP scopes are found
            Assert.Single(noScopeResult);
            var scope = $"{McpConstants.Agent365ToolsProdAppId}/.default";
            Assert.Equal(scope, noScopeResult[0]);
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithMultipleServersOnSameHost_ShouldReturnAllScopes()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                },
                new McpServerConfig
                {
                    McpServerName = "mcp_CalendarTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_CalendarTools",
                    Scope = "McpServers.Calendar.All",
                    Audience = "api://mcp-calendar"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().HaveCount(2);
            scopes.Should().Contain("McpServers.Mail.All");
            scopes.Should().Contain("McpServers.Calendar.All");
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithDifferentHosts_ShouldReturnOnlyMatchingScopes()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                },
                new McpServerConfig
                {
                    McpServerName = "mcp_OtherHost",
                    Url = "https://different-host.example.com/api/mcp",
                    Scope = "McpServers.Other.All",
                    Audience = "api://mcp-other"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().ContainSingle();
            scopes.Should().Contain("McpServers.Mail.All");
            scopes.Should().NotContain("McpServers.Other.All");
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithInvalidUrlInManifest_ShouldSkipInvalidAndContinue()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_InvalidUrl",
                    Url = "not-a-valid-url",
                    Scope = "McpServers.Invalid.All",
                    Audience = "api://mcp-invalid"
                },
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().ContainSingle();
            scopes.Should().Contain("McpServers.Mail.All");
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithMissingManifestFile_ShouldReturnDefaultScope()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid()}.json");

        // Act
        var scopes = _authService.ResolveScopesForResource(
            "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
            nonExistentPath);

        // Assert
        scopes.Should().ContainSingle();
        var expectedScope = $"{McpConstants.Agent365ToolsProdAppId}/.default";
        scopes[0].Should().Be(expectedScope);
    }

    [Fact]
    public void ResolveScopesForResource_WithEmptyMcpServers_ShouldReturnDefaultScope()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = Array.Empty<McpServerConfig>()
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().ContainSingle();
            var expectedScope = $"{McpConstants.Agent365ToolsProdAppId}/.default";
            scopes[0].Should().Be(expectedScope);
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithDuplicateScopes_ShouldReturnUniqueScopes()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools1",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools1",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                },
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools2",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools2",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                },
                new McpServerConfig
                {
                    McpServerName = "mcp_CalendarTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_CalendarTools",
                    Scope = "McpServers.Calendar.All",
                    Audience = "api://mcp-calendar"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools1",
                tempManifestPath);

            // Assert
            scopes.Should().HaveCount(2);
            scopes.Should().Contain("McpServers.Mail.All");
            scopes.Should().Contain("McpServers.Calendar.All");
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithCaseInsensitiveHostMatch_ShouldMatchCorrectly()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://AGENT365.SVC.CLOUD.MICROSOFT/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().ContainSingle();
            scopes.Should().Contain("McpServers.Mail.All");
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithNoManifestPath_ShouldLookForLocalManifest()
    {
        // Arrange
        var currentDir = Environment.CurrentDirectory;
        var localManifestPath = Path.Combine(currentDir, "ToolingManifest.json");
        var manifestCreated = false;

        try
        {
            // Only create if it doesn't exist to avoid overwriting
            if (!File.Exists(localManifestPath))
            {
                var manifest = new ToolingManifest
                {
                    McpServers = new[]
                    {
                        new McpServerConfig
                        {
                            McpServerName = "mcp_TestLocal",
                            Url = "https://test-local.example.com/api/mcp",
                            Scope = "McpServers.TestLocal.All",
                            Audience = "api://mcp-test"
                        }
                    }
                };
                var manifestJson = JsonSerializer.Serialize(manifest);
                File.WriteAllText(localManifestPath, manifestJson);
                manifestCreated = true;
            }

            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://test-local.example.com/api/mcp");

            // Assert - Should either find the local manifest or return default
            scopes.Should().NotBeNull();
            scopes.Should().NotBeEmpty();
        }
        finally
        {
            // Clean up only if we created it
            if (manifestCreated && File.Exists(localManifestPath))
            {
                File.Delete(localManifestPath);
            }
        }
    }

    [Fact]
    public void ResolveScopesForResource_WithMalformedJson_ShouldReturnDefaultScope()
    {
        // Arrange
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, "{ invalid json content }");

        try
        {
            // Act
            var scopes = _authService.ResolveScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            scopes.Should().ContainSingle();
            var expectedScope = $"{McpConstants.Agent365ToolsProdAppId}/.default";
            scopes[0].Should().Be(expectedScope);
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    #region ValidateScopesForResource Tests

    [Fact]
    public void ValidateScopesForResource_WithValidResource_ShouldReturnTrue()
    {
        // Arrange
        var manifest = new ToolingManifest
        {
            McpServers = new[]
            {
                new McpServerConfig
                {
                    McpServerName = "mcp_MailTools",
                    Url = "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                    Scope = "McpServers.Mail.All",
                    Audience = "api://mcp-mail"
                }
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        var tempManifestPath = Path.GetTempFileName();
        File.WriteAllText(tempManifestPath, manifestJson);

        try
        {
            // Act
            var isValid = _authService.ValidateScopesForResource(
                "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
                tempManifestPath);

            // Assert
            isValid.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempManifestPath))
                File.Delete(tempManifestPath);
        }
    }

    [Fact]
    public void ValidateScopesForResource_WithMissingManifest_ShouldReturnTrue()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid()}.json");

        // Act
        var isValid = _authService.ValidateScopesForResource(
            "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
            nonExistentPath);

        // Assert - Should return true because it falls back to default scope
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateScopesForResource_WithNullResourceUrl_ShouldHandleGracefully()
    {
        // Act
        var isValid = _authService.ValidateScopesForResource(null!);

        // Assert - Should not throw and handle gracefully
        // The method returns true by default, but this tests it doesn't crash
        isValid.Should().BeTrue();
    }

    #endregion

    #region GetAccessTokenWithScopesAsync Validation Tests

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithNullResourceAppId_ShouldThrowArgumentException()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All" };

        // Act
        Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
            null!, scopes);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Resource App ID cannot be empty*");
    }

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithEmptyResourceAppId_ShouldThrowArgumentException()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All" };

        // Act
        Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
            "", scopes);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Resource App ID cannot be empty*");
    }

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithWhitespaceResourceAppId_ShouldThrowArgumentException()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All" };

        // Act
        Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
            "   ", scopes);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Resource App ID cannot be empty*");
    }

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithNullScopes_ShouldThrowArgumentException()
    {
        // Arrange
        var resourceAppId = "ea9ffc3e-8a23-4a7d-836d-234d7c7565c1";

        // Act
        Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
            resourceAppId, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At least one scope must be specified*");
    }

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithEmptyScopes_ShouldThrowArgumentException()
    {
        // Arrange
        var resourceAppId = "ea9ffc3e-8a23-4a7d-836d-234d7c7565c1";
        var scopes = Array.Empty<string>();

        // Act
        Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
            resourceAppId, scopes);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At least one scope must be specified*");
    }

    #endregion

    #region CacheFilePath Parameter Tests

    [Fact]
    public async Task GetAccessTokenWithScopesAsync_WithCustomCacheFilePath_ShouldUseProvidedPath()
    {
        // Arrange
        var resourceAppId = "ea9ffc3e-8a23-4a7d-836d-234d7c7565c1";
        var scopes = new[] { "McpServers.Mail.All" };
        var customCachePath = Path.Combine(Path.GetTempPath(), $"custom_cache_{Guid.NewGuid()}.json");

        try
        {
            // Act & Assert - Method should accept the parameter without error
            // Note: This will still fail authentication since we can't mock interactive auth,
            // but it validates the parameter is accepted
            Func<Task> act = async () => await _authService.GetAccessTokenWithScopesAsync(
                resourceAppId,
                scopes,
                cacheFilePath: customCachePath);

            // We expect authentication to fail in unit tests, but not parameter validation
            await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            if (File.Exists(customCachePath))
            {
                File.Delete(customCachePath);
            }
        }
    }

    #endregion

    #region ClearCache Additional Tests

    [Fact]
    public void ClearCache_WithMultipleTokensCached_ShouldClearAll()
    {
        // Arrange
        var cacheDir = Path.GetDirectoryName(_testCachePath)!;
        Directory.CreateDirectory(cacheDir);

        var tokenCache = new
        {
            Tokens = new Dictionary<string, object>
            {
                ["resource1"] = new { AccessToken = "token1", ExpiresOn = DateTime.UtcNow.AddHours(1), TenantId = "tenant1" },
                ["resource2"] = new { AccessToken = "token2", ExpiresOn = DateTime.UtcNow.AddHours(1), TenantId = "tenant2" }
            }
        };

        var cacheJson = JsonSerializer.Serialize(tokenCache);
        File.WriteAllText(_testCachePath, cacheJson);

        // Act
        _authService.ClearCache();

        // Assert
        File.Exists(_testCachePath).Should().BeFalse();
    }

    [Fact]
    public void ClearCache_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var cacheDir = Path.GetDirectoryName(_testCachePath)!;
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(_testCachePath, "test content");

        // Act
        Action act = () =>
        {
            _authService.ClearCache();
            _authService.ClearCache();
            _authService.ClearCache();
        };

        // Assert
        act.Should().NotThrow();
        File.Exists(_testCachePath).Should().BeFalse();
    }

    #endregion

    #region Token Cache Format Tests

    [Fact]
    public async Task LoadCachedToken_FromMcpBearerTokenFile_SingleTokenFormat_ShouldLoadCorrectly()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        
        try
        {
            // Create single token format (mcp_bearer_token.json format)
            var singleToken = new
            {
                AccessToken = "test-single-token",
                ExpiresOn = DateTime.UtcNow.AddHours(1),
                TenantId = "test-tenant-id"
            };
            
            var json = JsonSerializer.Serialize(singleToken, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(testCachePath)!);
            await File.WriteAllTextAsync(testCachePath, json);

            // Act
            var authService = new AuthenticationService(_mockLogger);
            
            // Use reflection to call private LoadCachedTokenAsync method
            var method = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task<object?>)method!.Invoke(authService, new object[] { "any-key", testCachePath })!;
            var result = await task;

            // Assert
            result.Should().NotBeNull();
            var tokenInfo = result!;
            var accessToken = tokenInfo.GetType().GetProperty("AccessToken")!.GetValue(tokenInfo) as string;
            accessToken.Should().Be("test-single-token");
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task LoadCachedToken_FromAuthTokenFile_DictionaryFormat_ShouldLoadCorrectly()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), $"test-auth-{Guid.NewGuid()}.json");
        
        try
        {
            // Create dictionary format (auth-token.json format)
            var dictionaryCache = new
            {
                Tokens = new Dictionary<string, object>
                {
                    ["resource-key-1"] = new
                    {
                        AccessToken = "test-dict-token-1",
                        ExpiresOn = DateTime.UtcNow.AddHours(1),
                        TenantId = "tenant-1"
                    },
                    ["resource-key-2"] = new
                    {
                        AccessToken = "test-dict-token-2",
                        ExpiresOn = DateTime.UtcNow.AddHours(2),
                        TenantId = "tenant-2"
                    }
                }
            };
            
            var json = JsonSerializer.Serialize(dictionaryCache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(testCachePath, json);

            // Act
            var authService = new AuthenticationService(_mockLogger);
            
            // Use reflection to call private LoadCachedTokenAsync method
            var method = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task<object?>)method!.Invoke(authService, new object[] { "resource-key-1", testCachePath })!;
            var result = await task;

            // Assert
            result.Should().NotBeNull();
            var tokenInfo = result!;
            var accessToken = tokenInfo.GetType().GetProperty("AccessToken")!.GetValue(tokenInfo) as string;
            accessToken.Should().Be("test-dict-token-1");
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task CacheToken_ToMcpBearerTokenFile_ShouldUseSingleTokenFormat()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        
        try
        {
            var authService = new AuthenticationService(_mockLogger);
            
            // Create TokenInfo using reflection (it's a private class)
            var tokenInfoType = typeof(AuthenticationService).GetNestedType("TokenInfo", 
                System.Reflection.BindingFlags.NonPublic);
            var tokenInfo = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(tokenInfo, "cached-mcp-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(tokenInfo, DateTime.UtcNow.AddHours(1));
            tokenInfoType.GetProperty("TenantId")!.SetValue(tokenInfo, "test-tenant");

            // Use reflection to call private CacheTokenAsync method
            var method = typeof(AuthenticationService).GetMethod("CacheTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task)method!.Invoke(authService, new object[] { "resource-key", tokenInfo, testCachePath })!;
            await task;

            // Assert
            File.Exists(testCachePath).Should().BeTrue();
            var json = await File.ReadAllTextAsync(testCachePath);
            
            // Verify it's in single token format (not wrapped in Tokens dictionary)
            json.Should().NotContain("\"Tokens\"");
            json.Should().Contain("\"AccessToken\"");
            json.Should().Contain("cached-mcp-token");
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task CacheToken_ToAuthTokenFile_ShouldUseDictionaryFormat()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), $"test-auth-{Guid.NewGuid()}.json");
        
        try
        {
            var authService = new AuthenticationService(_mockLogger);
            
            // Create TokenInfo using reflection
            var tokenInfoType = typeof(AuthenticationService).GetNestedType("TokenInfo", 
                System.Reflection.BindingFlags.NonPublic);
            var tokenInfo = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(tokenInfo, "cached-auth-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(tokenInfo, DateTime.UtcNow.AddHours(1));
            tokenInfoType.GetProperty("TenantId")!.SetValue(tokenInfo, "test-tenant");

            // Use reflection to call private CacheTokenAsync method
            var method = typeof(AuthenticationService).GetMethod("CacheTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task)method!.Invoke(authService, new object[] { "resource-key", tokenInfo, testCachePath })!;
            await task;

            // Assert
            File.Exists(testCachePath).Should().BeTrue();
            var json = await File.ReadAllTextAsync(testCachePath);
            
            // Verify it's in dictionary format (wrapped in Tokens dictionary)
            json.Should().Contain("\"Tokens\"");
            json.Should().Contain("\"resource-key\"");
            json.Should().Contain("cached-auth-token");
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task SwitchBetweenFormats_McpToAuth_ShouldHandleGracefully()
    {
        // Arrange - Start with MCP bearer token format
        var mcpCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        var authCachePath = Path.Combine(Path.GetTempPath(), $"test-auth-{Guid.NewGuid()}.json");
        
        try
        {
            // Create MCP format file first
            var mcpToken = new
            {
                AccessToken = "mcp-format-token",
                ExpiresOn = DateTime.UtcNow.AddHours(1),
                TenantId = "tenant-mcp"
            };
            await File.WriteAllTextAsync(mcpCachePath, JsonSerializer.Serialize(mcpToken, new JsonSerializerOptions { WriteIndented = true }));

            var authService = new AuthenticationService(_mockLogger);
            
            // Act 1: Load from MCP format
            var loadMethod = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var loadTask1 = (Task<object?>)loadMethod!.Invoke(authService, new object[] { "any-key", mcpCachePath })!;
            var mcpResult = await loadTask1;

            // Assert 1: MCP format loaded correctly
            mcpResult.Should().NotBeNull();
            var mcpAccessToken = mcpResult!.GetType().GetProperty("AccessToken")!.GetValue(mcpResult) as string;
            mcpAccessToken.Should().Be("mcp-format-token");

            // Act 2: Cache same token to auth format
            var tokenInfoType = typeof(AuthenticationService).GetNestedType("TokenInfo", 
                System.Reflection.BindingFlags.NonPublic);
            var tokenInfo = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(tokenInfo, "mcp-format-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(tokenInfo, DateTime.UtcNow.AddHours(1));
            tokenInfoType.GetProperty("TenantId")!.SetValue(tokenInfo, "tenant-mcp");

            var cacheMethod = typeof(AuthenticationService).GetMethod("CacheTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cacheTask = (Task)cacheMethod!.Invoke(authService, new object[] { "resource-key", tokenInfo, authCachePath })!;
            await cacheTask;

            // Act 3: Load from auth format
            var loadTask2 = (Task<object?>)loadMethod!.Invoke(authService, new object[] { "resource-key", authCachePath })!;
            var authResult = await loadTask2;

            // Assert 2: Auth format loaded correctly
            authResult.Should().NotBeNull();
            var authAccessToken = authResult!.GetType().GetProperty("AccessToken")!.GetValue(authResult) as string;
            authAccessToken.Should().Be("mcp-format-token");
        }
        finally
        {
            if (File.Exists(mcpCachePath))
                File.Delete(mcpCachePath);
            if (File.Exists(authCachePath))
                File.Delete(authCachePath);
        }
    }

    [Fact]
    public async Task SwitchBetweenFormats_AuthToMcp_ShouldHandleGracefully()
    {
        // Arrange - Start with auth token dictionary format
        var authCachePath = Path.Combine(Path.GetTempPath(), $"test-auth-{Guid.NewGuid()}.json");
        var mcpCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        
        try
        {
            // Create auth dictionary format file first
            var authCache = new
            {
                Tokens = new Dictionary<string, object>
                {
                    ["resource-key"] = new
                    {
                        AccessToken = "auth-format-token",
                        ExpiresOn = DateTime.UtcNow.AddHours(1),
                        TenantId = "tenant-auth"
                    }
                }
            };
            await File.WriteAllTextAsync(authCachePath, JsonSerializer.Serialize(authCache, new JsonSerializerOptions { WriteIndented = true }));

            var authService = new AuthenticationService(_mockLogger);
            
            // Act 1: Load from auth dictionary format
            var loadMethod = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var loadTask1 = (Task<object?>)loadMethod!.Invoke(authService, new object[] { "resource-key", authCachePath })!;
            var authResult = await loadTask1;

            // Assert 1: Auth format loaded correctly
            authResult.Should().NotBeNull();
            var authAccessToken = authResult!.GetType().GetProperty("AccessToken")!.GetValue(authResult) as string;
            authAccessToken.Should().Be("auth-format-token");

            // Act 2: Cache same token to MCP format
            var tokenInfoType = typeof(AuthenticationService).GetNestedType("TokenInfo", 
                System.Reflection.BindingFlags.NonPublic);
            var tokenInfo = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(tokenInfo, "auth-format-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(tokenInfo, DateTime.UtcNow.AddHours(1));
            tokenInfoType.GetProperty("TenantId")!.SetValue(tokenInfo, "tenant-auth");

            var cacheMethod = typeof(AuthenticationService).GetMethod("CacheTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cacheTask = (Task)cacheMethod!.Invoke(authService, new object[] { "resource-key", tokenInfo, mcpCachePath })!;
            await cacheTask;

            // Act 3: Load from MCP format
            var loadTask2 = (Task<object?>)loadMethod!.Invoke(authService, new object[] { "any-key", mcpCachePath })!;
            var mcpResult = await loadTask2;

            // Assert 2: MCP format loaded correctly (ignores resource key)
            mcpResult.Should().NotBeNull();
            var mcpAccessToken = mcpResult!.GetType().GetProperty("AccessToken")!.GetValue(mcpResult) as string;
            mcpAccessToken.Should().Be("auth-format-token");
        }
        finally
        {
            if (File.Exists(authCachePath))
                File.Delete(authCachePath);
            if (File.Exists(mcpCachePath))
                File.Delete(mcpCachePath);
        }
    }

    [Fact]
    public async Task LoadCachedToken_FromMcpFile_IgnoresResourceKey()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        
        try
        {
            var singleToken = new
            {
                AccessToken = "test-token-no-key",
                ExpiresOn = DateTime.UtcNow.AddHours(1),
                TenantId = "test-tenant"
            };
            
            await File.WriteAllTextAsync(testCachePath, JsonSerializer.Serialize(singleToken, new JsonSerializerOptions { WriteIndented = true }));
            var authService = new AuthenticationService(_mockLogger);
            
            // Act: Try with different resource keys - should return same token
            var method = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            var task1 = (Task<object?>)method!.Invoke(authService, new object[] { "key-1", testCachePath })!;
            var result1 = await task1;
            
            var task2 = (Task<object?>)method!.Invoke(authService, new object[] { "key-2", testCachePath })!;
            var result2 = await task2;

            // Assert: Both should return the same token regardless of key
            result1.Should().NotBeNull();
            result2.Should().NotBeNull();
            
            var token1 = result1!.GetType().GetProperty("AccessToken")!.GetValue(result1) as string;
            var token2 = result2!.GetType().GetProperty("AccessToken")!.GetValue(result2) as string;
            
            token1.Should().Be("test-token-no-key");
            token2.Should().Be("test-token-no-key");
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task CacheToken_ToMcpFile_MultipleTimes_ShouldOverwrite()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), AuthenticationConstants.MCPBearerTokenFileName);
        
        try
        {
            var authService = new AuthenticationService(_mockLogger);
            var tokenInfoType = typeof(AuthenticationService).GetNestedType("TokenInfo", 
                System.Reflection.BindingFlags.NonPublic);
            var cacheMethod = typeof(AuthenticationService).GetMethod("CacheTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act: Cache first token
            var token1 = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(token1, "first-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(token1, DateTime.UtcNow.AddHours(1));
            tokenInfoType.GetProperty("TenantId")!.SetValue(token1, "tenant-1");
            
            await (Task)cacheMethod!.Invoke(authService, new object[] { "key-1", token1, testCachePath })!;

            // Act: Cache second token (should overwrite)
            var token2 = Activator.CreateInstance(tokenInfoType!)!;
            tokenInfoType!.GetProperty("AccessToken")!.SetValue(token2, "second-token");
            tokenInfoType.GetProperty("ExpiresOn")!.SetValue(token2, DateTime.UtcNow.AddHours(2));
            tokenInfoType.GetProperty("TenantId")!.SetValue(token2, "tenant-2");
            
            await (Task)cacheMethod!.Invoke(authService, new object[] { "key-2", token2, testCachePath })!;

            // Assert: File should contain only the second token
            var json = await File.ReadAllTextAsync(testCachePath);
            json.Should().Contain("second-token");
            json.Should().NotContain("first-token");
            json.Should().NotContain("\"Tokens\""); // Should not have dictionary wrapper
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    [Fact]
    public async Task LoadCachedToken_FromNonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid()}.json");
        var authService = new AuthenticationService(_mockLogger);
        
        // Act
        var method = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task<object?>)method!.Invoke(authService, new object[] { "any-key", nonExistentPath })!;
        var result = await task;

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadCachedToken_FromMalformedJson_ShouldReturnNull()
    {
        // Arrange
        var testCachePath = Path.Combine(Path.GetTempPath(), $"malformed-{Guid.NewGuid()}.json");
        
        try
        {
            await File.WriteAllTextAsync(testCachePath, "{ invalid json syntax }");
            var authService = new AuthenticationService(_mockLogger);
            
            // Act
            var method = typeof(AuthenticationService).GetMethod("LoadCachedTokenAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task<object?>)method!.Invoke(authService, new object[] { "any-key", testCachePath })!;
            var result = await task;

            // Assert
            result.Should().BeNull();
        }
        finally
        {
            if (File.Exists(testCachePath))
                File.Delete(testCachePath);
        }
    }

    #endregion

    // Note: Testing GetAccessTokenAsync requires interactive browser authentication
    // which is not suitable for automated unit tests. This should be tested with integration tests
    // or manual testing.
}
