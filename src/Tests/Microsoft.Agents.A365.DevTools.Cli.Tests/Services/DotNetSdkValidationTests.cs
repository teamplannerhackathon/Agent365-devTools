// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Commands.SetupSubcommands;
using Microsoft.Agents.A365.DevTools.Cli.Exceptions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

/// <summary>
/// Tests for .NET SDK version validation logic
/// Reproduces the intermittent failure issue from PR #130
/// </summary>
public class DotNetSdkValidationTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly CommandExecutor _commandExecutor;
    private readonly ITestOutputHelper _output;
    private readonly string _testProjectPath;

    public DotNetSdkValidationTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = Substitute.For<ILogger>();
        _commandExecutor = Substitute.For<CommandExecutor>(Substitute.For<ILogger<CommandExecutor>>());
        
        // Create a temporary test project directory
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectPath);
    }

    /// <summary>
    /// Reproduces the intermittent failure where dotnet --version command fails
    /// This simulates the race condition that can occur under system load
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_WhenDotNetVersionCommandFails_ThrowsDotNetSdkVersionMismatchException()
    {
        // Arrange - Create a test .csproj file targeting .NET 8.0
        var projectFile = CreateTestProject("net8.0");
        
        // Mock: dotnet --version command FAILS (simulating intermittent process spawn failure)
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(Task.FromResult(new CommandResult 
            { 
                ExitCode = 1,  // ? Command failed
                StandardError = "Process spawn failed"
            }));

        // Act & Assert
        var exception = Assert.Throws<DotNetSdkVersionMismatchException>(() =>
        {
            // Call the private static method using reflection
            var result = InvokeResolveDotNetRuntimeVersion(
                ProjectPlatform.DotNet, 
                _testProjectPath);
        });

        // Verify exception details
        exception.Should().NotBeNull();
        exception.Message.Should().Contain("The project targets .NET 8.0, but the required .NET SDK is not installed");
        
        _output.WriteLine($"? Test reproduced the issue: {exception.Message}");
    }

    /// <summary>
    /// Tests the scenario where SDK version is detected but validation logic has a bug
    /// This reproduces the exact error message from the user's report
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_WhenVersionDetectedButValidationFails_ShowsContradictoryError()
    {
        // Arrange - Create a test .csproj file targeting .NET 8.0
        var projectFile = CreateTestProject("net8.0");
        
        // Mock: dotnet --version returns 9.0.308 (which SHOULD work for .NET 8 projects)
        // But the command reports ExitCode != 0 (simulating intermittent failure)
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(Task.FromResult(new CommandResult 
            { 
                ExitCode = 1,  // ? Command failed even though it returned version
                StandardOutput = "9.0.308",  // ? Version detected
                StandardError = "Timeout"
            }));

        // Act & Assert
        var exception = Assert.Throws<DotNetSdkVersionMismatchException>(() =>
        {
            InvokeResolveDotNetRuntimeVersion(
                ProjectPlatform.DotNet, 
                _testProjectPath);
        });

        // This reproduces the contradictory error:
        // "Installed SDK version: 9.0.308" but still throws "SDK is not installed"
        exception.Message.Should().Contain("required .NET SDK is not installed");
        
        _output.WriteLine("? Reproduced contradictory error:");
        _output.WriteLine($"   Detected version in output: 9.0.308");
        _output.WriteLine($"   But exception still thrown: {exception.Message}");
    }

    /// <summary>
    /// Tests successful scenario - SDK 9.0 building .NET 8.0 project
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_WhenNewerSdkInstalled_SucceedsWithForwardCompatibility()
    {
        // Arrange
        var projectFile = CreateTestProject("net8.0");
        
        // Mock: dotnet --version returns 9.0.308 (newer than target)
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(Task.FromResult(new CommandResult 
            { 
                ExitCode = 0,  // ? Command succeeded
                StandardOutput = "9.0.308"
            }));

        // Act
        var version = InvokeResolveDotNetRuntimeVersion(
            ProjectPlatform.DotNet, 
            _testProjectPath);

        // Assert
        version.Should().Be("8.0");
        
        _output.WriteLine($"? Forward compatibility works: SDK 9.0.308 can build .NET 8.0");
    }

    /// <summary>
    /// Tests scenario where installed SDK is older than target framework
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_WhenOlderSdkInstalled_ThrowsDotNetSdkVersionMismatchException()
    {
        // Arrange
        var projectFile = CreateTestProject("net9.0");
        
        // Mock: dotnet --version returns 8.0.100 (older than target)
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(Task.FromResult(new CommandResult 
            { 
                ExitCode = 0,
                StandardOutput = "8.0.100"
            }));

        // Act & Assert
        var exception = Assert.Throws<DotNetSdkVersionMismatchException>(() =>
        {
            InvokeResolveDotNetRuntimeVersion(
                ProjectPlatform.DotNet, 
                _testProjectPath);
        });

        exception.Message.Should().Contain("targets .NET 9.0");
        exception.Message.Should().Contain("Installed SDK version: 8.0.100");
        
        _output.WriteLine($"? Correctly detected incompatible SDK: {exception.Message}");
    }

    /// <summary>
    /// Stress test - Validates that retry logic handles intermittent failures gracefully
    /// With the retry fix, first attempt fails but retry succeeds
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_UnderLoad_ShouldHandleGracefully()
    {
        // Arrange
        CreateTestProject("net8.0");
        
        var callCount = 0;
        var lockObj = new object();
        
        // Mock: First 3 calls fail, then succeed (simulates retry succeeding after initial failure)
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(callInfo =>
            {
                int currentCall;
                lock (lockObj)
                {
                    callCount++;
                    currentCall = callCount;
                }
                
                // First 3 attempts fail (representing 1 initial attempt + 2 retries for first call)
                // Then all subsequent calls succeed
                var shouldFail = currentCall <= 3;
                
                return Task.FromResult(new CommandResult 
                { 
                    ExitCode = shouldFail ? 1 : 0,
                    StandardOutput = shouldFail ? "" : "9.0.308",
                    StandardError = shouldFail ? "Intermittent failure" : ""
                });
            });

        // Act - Call the method (will retry on failure)
        string? result = null;
        Exception? caughtException = null;
        
        try
        {
            result = InvokeResolveDotNetRuntimeVersion(
                ProjectPlatform.DotNet, 
                _testProjectPath);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert - With 3-attempt retry logic:
        // - First attempt fails (call 1)
        // - Second attempt fails (call 2)
        // - Third attempt fails (call 3)
        // - Should eventually throw exception after all retries exhausted
        caughtException.Should().BeOfType<DotNetSdkVersionMismatchException>(
            "All 3 retry attempts fail, so exception should be thrown");
        
        callCount.Should().Be(3, "Should have attempted 3 times before giving up");
        
        _output.WriteLine($"? Retry logic working: Made {callCount} attempts before giving up");
        _output.WriteLine($"This correctly demonstrates retry behavior with persistent failures");
        
        // Now test successful retry scenario
        callCount = 0;
        
        // Mock: First call fails, but retry succeeds
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(callInfo =>
            {
                int currentCall;
                lock (lockObj)
                {
                    callCount++;
                    currentCall = callCount;
                }
                
                // Only first attempt fails, second succeeds
                var shouldFail = currentCall == 1;
                
                return Task.FromResult(new CommandResult 
                { 
                    ExitCode = shouldFail ? 1 : 0,
                    StandardOutput = shouldFail ? "" : "9.0.308",
                    StandardError = shouldFail ? "Intermittent failure" : ""
                });
            });

        // Act - Second test with successful retry
        result = InvokeResolveDotNetRuntimeVersion(
            ProjectPlatform.DotNet, 
            _testProjectPath);

        // Assert - Should succeed on retry
        result.Should().Be("8.0", "Retry should succeed and detect .NET 8.0");
        callCount.Should().Be(2, "Should fail once then succeed on retry");
        
        _output.WriteLine($"? Successful retry: Failed once, succeeded on attempt 2");
    }

    /// <summary>
    /// Test with malformed version output
    /// </summary>
    [Fact]
    public void ResolveDotNetRuntimeVersion_WhenVersionOutputMalformed_HandlesGracefully()
    {
        // Arrange
        CreateTestProject("net8.0");
        
        // Mock: dotnet --version returns malformed output
        _commandExecutor.ExecuteAsync("dotnet", "--version", captureOutput: true)
            .Returns(Task.FromResult(new CommandResult 
            { 
                ExitCode = 0,
                StandardOutput = "invalid-version-format"  // ? Malformed
            }));

        // Act
        var version = InvokeResolveDotNetRuntimeVersion(
            ProjectPlatform.DotNet, 
            _testProjectPath);

        // Assert - Should still return detected target version
        version.Should().Be("8.0");
        
        _output.WriteLine("? Gracefully handled malformed SDK version output");
    }

    #region Helper Methods

    private string CreateTestProject(string targetFramework)
    {
        var projectFile = Path.Combine(_testProjectPath, "TestProject.csproj");
        
        var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{targetFramework}</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>";

        File.WriteAllText(projectFile, projectContent);
        
        _output.WriteLine($"Created test project: {projectFile}");
        _output.WriteLine($"Target framework: {targetFramework}");
        
        return projectFile;
    }

    private string? InvokeResolveDotNetRuntimeVersion(
        ProjectPlatform platform, 
        string projectPath)
    {
        // Use reflection to call the private static method
        var infrastructureType = typeof(InfrastructureSubcommand);
        var method = infrastructureType.GetMethod(
            "ResolveDotNetRuntimeVersion", 
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("ResolveDotNetRuntimeVersion method not found");
        }

        try
        {
            var result = method.Invoke(null, new object[] 
            { 
                platform, 
                projectPath, 
                _commandExecutor, 
                _logger 
            });
            
            return result as string;
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap the actual exception thrown by the method
            if (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
            throw;
        }
    }

    #endregion

    public void Dispose()
    {
        // Cleanup test project directory
        if (Directory.Exists(_testProjectPath))
        {
            try
            {
                Directory.Delete(_testProjectPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
