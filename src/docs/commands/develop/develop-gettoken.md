# a365 develop gettoken Command

## Overview

The `a365 develop gettoken` command retrieves bearer tokens for testing MCP (Model Context Protocol) server authentication during development. This command acquires tokens with explicit scopes using interactive browser authentication.

> **Note**: For production agent deployments, authentication is handled automatically through inheritable permissions configured during `a365 setup permissions mcp`. This command is for development testing and debugging.

## Usage

```bash
a365 develop gettoken [options]
```

## Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--config` | `-c` | Configuration file path | `a365.config.json` |
| `--app-id` | | Application (client) ID for authentication | ClientAppId from config |
| `--manifest` | `-m` | Path to ToolingManifest.json | `<deploymentProjectPath>/ToolingManifest.json` |
| `--scopes` | | Specific scopes to request (space-separated) | Read from ToolingManifest.json |
| `--output` | `-o` | Output format: table, json, or raw | `table` |
| `--verbose` | `-v` | Show detailed output including full token | `false` |
| `--force-refresh` | | Force token refresh bypassing cache | `false` |

## When to Use This Command

### Development & Testing
- Local development and debugging
- Manual API testing with Postman/curl
- Integration testing before deployment

### NOT for Production
- Production agents use inheritable permissions (`a365 setup permissions mcp`)

## Prerequisites

1. **Azure CLI**: Run `az login` before using this command
2. **Configuration or Client App ID**: Either `a365.config.json` with `clientAppId` or use `--app-id` parameter
3. **ToolingManifest.json** (optional): Can be bypassed with `--scopes` parameter

## ToolingManifest.json Structure

```json
{
  "mcpServers": [
    {
      "mcpServerName": "mcp_MailTools",
      "scope": "McpServers.Mail.All"
    },
    {
      "mcpServerName": "mcp_CalendarTools",
      "scope": "McpServers.Calendar.All"
    }
  ]
}
```

## Examples

### Get token with all scopes from manifest
```bash
a365 develop gettoken
```

### Get token with specific scopes
```bash
a365 develop gettoken --scopes McpServers.Mail.All McpServers.Calendar.All
```

### Get token with custom client app
```bash
a365 develop gettoken --app-id 98765432-4321-4321-4321-210987654321
```

### Export token to file
```bash
a365 develop gettoken --output raw > token.txt
```

### Use token in curl request
```bash
TOKEN=$(a365 develop gettoken --output raw)
curl -H "Authorization: Bearer $TOKEN" https://agent365.svc.cloud.microsoft/agents/discoverToolServers
```

## Authentication Flow

1. **Client Application**: Uses `--app-id` or `ClientAppId` from config
2. **Scope Resolution**: Uses `--scopes` or reads from `ToolingManifest.json`
3. **Token Acquisition**: Opens browser for interactive OAuth2 authentication
4. **Token Caching**: Cached in local storage for reuse (until expiration or `--force-refresh`)