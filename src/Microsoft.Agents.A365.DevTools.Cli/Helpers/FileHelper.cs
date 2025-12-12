// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Helpers;

/// <summary>
/// Helper methods for file operations including cross-platform file opening
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Opens a file in the user's default editor in a cross-platform way
    /// </summary>
    /// <param name="filePath">Absolute path to the file to open</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <returns>True if file was opened successfully or attempt was made, false if file doesn't exist</returns>
    public static bool TryOpenFileInDefaultEditor(string filePath, ILogger logger)
    {
        if (!File.Exists(filePath))
        {
            logger.LogError("File not found: {FilePath}", filePath);
            return false;
        }

        try
        {
            // On Unix-like systems, respect EDITOR or VISUAL environment variables
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var editor = Environment.GetEnvironmentVariable("EDITOR")
                            ?? Environment.GetEnvironmentVariable("VISUAL");

                if (!string.IsNullOrEmpty(editor))
                {
                    logger.LogDebug("Using editor from environment: {Editor}", editor);
                    Process.Start(editor, $"\"{filePath}\"");
                    logger.LogInformation("Opened file in {Editor}", editor);
                    return true;
                }
            }

            // Platform-specific default open
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                logger.LogInformation("Opened file in default Windows editor");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{filePath}\"");
                logger.LogInformation("Opened file using macOS 'open' command");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Try xdg-open which is standard on most Linux distros
                Process.Start("xdg-open", $"\"{filePath}\"");
                logger.LogInformation("Opened file using Linux 'xdg-open' command");
            }
            else
            {
                logger.LogWarning("Unsupported platform for automatic file opening");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not automatically open file: {Error}", ex.Message);
            logger.LogInformation("Please manually open the file to edit it");
            return false;
        }
    }
}
