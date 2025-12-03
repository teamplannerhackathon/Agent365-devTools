// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.IO;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Helpers;

/// <summary>
/// Custom console formatter that outputs clean messages without timestamps or category names.
/// Follows Azure CLI output patterns for user-friendly CLI experience.
/// Errors are displayed in red, warnings in yellow, info is plain text.
/// </summary>
public sealed class CleanConsoleFormatter : ConsoleFormatter
{
    private readonly SimpleConsoleFormatterOptions _options;

    public CleanConsoleFormatter(Microsoft.Extensions.Options.IOptionsMonitor<SimpleConsoleFormatterOptions> options) 
        : base("clean")
    {
        _options = options.CurrentValue;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        // Azure CLI pattern: red for errors, yellow for warnings, no color for info
        switch (logEntry.LogLevel)
        {
            case LogLevel.Error:
            case LogLevel.Critical:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("ERROR: ");
                Console.Write(message);
                Console.ResetColor();
                Console.WriteLine();
                break;
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("WARNING: ");
                Console.Write(message);
                Console.ResetColor();
                Console.WriteLine();
                break;
            default:
                Console.WriteLine(message);
                break;
        }

        // If there's an exception, include it (for debugging)
        if (logEntry.Exception != null)
        {
            Console.ForegroundColor = logEntry.LogLevel switch
            {
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                _ => Console.ForegroundColor
            };
            Console.WriteLine(logEntry.Exception.ToString());
            Console.ResetColor();
        }
    }
}
