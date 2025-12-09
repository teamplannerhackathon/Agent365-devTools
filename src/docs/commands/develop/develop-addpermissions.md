# a365 develop addpermissions Command

## Overview

The `a365 develop addpermissions` command adds MCP (Model Context Protocol) server API permissions to Azure AD applications. This command is designed for **development scenarios** where you need to configure custom applications (not agent blueprints) to access MCP servers.

> **Note**: For production agent blueprint configuration, use `a365 setup permissions mcp` instead, which configures inheritable permissions and OAuth2 grants for the agent blueprint.

## Usage

```bash
a365 develop addpermissions [options]
```

## Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--config` | `-c` | Configuration file path | `a365.config.json` |
| `--manifest` | `-m` | Path to ToolingManifest.json | `<deploymentProjectPath>/ToolingManifest.json` |
| `--app-id` | | Application (client) ID to add permissions to | Blueprint ID from config |
| `--scopes` | | Specific scopes to add (space-separated) | All scopes from ToolingManifest.json |
| `--verbose` | `-v` | Show detailed output | `false` |
| `--dry-run` | | Show what would be done without making changes | `false` |

## When to Use This Command

### Development Scenarios
- Custom backend services needing MCP access
- Testing applications with specific MCP permissions
- Third-party integrations calling MCP servers

### NOT for Agent Blueprints
- Use `a365 setup permissions mcp` for agent blueprint setup

## Prerequisites

1. **Azure CLI Authentication**: `az login` with `Application.ReadWrite.All` permission
2. **Target Application**: Application must exist in Azure AD
3. **Configuration or App ID**: Either `a365.config.json` or `--app-id` parameter

## ToolingManifest.json Structure

```json
{
  "mcpServers": [
    {
      "mcpServerName": "mcp_MailTools",
      "url": "https://agent365.svc.cloud.microsoft/agents/servers/mcp_MailTools",
      "scope": "McpServers.Mail.All",
      "audience": "api://mcp-mailtools"
    },
    {
      "mcpServerName": "mcp_CalendarTools",
      "url": "https://agent365.svc.cloud.microsoft/agents/servers/mcp_CalendarTools",
      "scope": "McpServers.Calendar.All",
      "audience": "api://mcp-calendartools"
    }
  ]
}
```

## Examples

### Add all scopes from manifest
```bash
a365 develop addpermissions
```

### Add permissions to custom application
```bash
a365 develop addpermissions --app-id 12345678-1234-1234-1234-123456789abc
```

### Add specific scopes only
```bash
a365 develop addpermissions --scopes McpServers.Mail.All McpServers.Calendar.All
```

### Combine options
```bash
a365 develop addpermissions --app-id 12345678-1234-1234-1234-123456789abc --scopes McpServers.Mail.All --dry-run
```