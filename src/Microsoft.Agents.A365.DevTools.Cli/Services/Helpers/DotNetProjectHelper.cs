// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Helpers;

/// <summary>
/// Helper class for inspecting .NET project metadata.
/// </summary>
public static class DotNetProjectHelper
{
    /// <summary>
    /// Detects the target .NET runtime version (e.g. "8.0", "9.0") from a .csproj file.
    /// Supports both TargetFramework and TargetFrameworks.
    /// </summary>
    public static string? DetectTargetRuntimeVersion(string projectFilePath, ILogger logger)
    {
        if (!File.Exists(projectFilePath))
        {
            logger.LogWarning("Project file not found: {Path}", projectFilePath);
            return null;
        }

        var content = File.ReadAllText(projectFilePath);

        // Match <TargetFramework> or <TargetFrameworks>
        var tfmMatch = Regex.Match(
            content,
            @"<TargetFrameworks?>\s*([^<]+)\s*</TargetFrameworks?>",
            RegexOptions.IgnoreCase);

        if (!tfmMatch.Success)
        {
            logger.LogWarning("No TargetFramework(s) found in project file: {Path}", projectFilePath);
            return null;
        }

        // If multiple TFMs are specified, pick the first one
        // (future improvement: pick highest)
        var tfms = tfmMatch.Groups[1].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tfm = tfms.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tfm))
        {
            return null;
        }

        // Match net8.0, net9.0, net9.0-windows, etc.
        var verMatch = Regex.Match(
            tfm,
            @"net(\d+)\.(\d+)",
            RegexOptions.IgnoreCase);

        if (!verMatch.Success)
        {
            logger.LogWarning("Unrecognized TargetFramework format: {Tfm}", tfm);
            return null;
        }

        var version = $"{verMatch.Groups[1].Value}.{verMatch.Groups[2].Value}";
        logger.LogInformation(
            "Detected TargetFramework: {Tfm} → .NET {Version}",
            tfm,
            version);

        return version;
    }
}
