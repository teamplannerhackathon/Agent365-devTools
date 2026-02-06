# Agent 365 CLI Setup Instructions for AI Agents

---

## BEFORE YOU BEGIN — MANDATORY EXECUTION PROTOCOL

> **YOUR FIRST AND ONLY ACTION RIGHT NOW:** Call `manage_todo_list` (or equivalent) to create the 5 todos listed below. Then mark Todo 1 in-progress and jump to Step 1. **Do NOT read further. Do NOT run any commands. Do NOT gather values. Do NOT ask questions.**

**RULE 1 — CREATE EXACTLY 5 TODOS:**

- Todo 1: `Step 1: Verify and Install/Update the Agent 365 CLI`
- Todo 2: `Step 2: Ensure Prerequisites and Environment Configuration`
- Todo 3: `Step 3: Configure the Agent 365 CLI (Initialize Configuration)`
- Todo 4: `Step 4: Run Agent 365 Setup to Provision Prerequisites`
- Todo 5: `Step 5: Publish and Deploy the Agent Application`

**RULE 2 — ALWAYS BEGIN FROM STEP 1.** No step is optional. Even if the CLI appears installed or Azure appears logged in, you MUST run the validation commands in each step.

**RULE 3 — SUB-SECTIONS ARE NOT SEPARATE TODOS.** Each `## Step` has internal sub-sections — these are tasks WITHIN that step, NOT separate todos. Exactly 5 todos total.

**RULE 4 — ONE STEP AT A TIME.** Complete each step fully. Mark its todo in-progress when starting, complete when done. Do NOT run `az account show`, ask about deployment type, or gather Azure values — those belong to Step 3, which comes AFTER Steps 1 and 2.

**RULE 5 — INPUT FIELDS.** In Step 3, present exactly 5 fields (Azure-hosted) or 2 fields (self-hosted). The `clientAppId` is collected in Step 2 — do NOT ask for it again.

---

## Step 1: Verify and Install/Update the Agent 365 CLI

> Mark Todo 1 as **in-progress** now.

### Check .NET and CLI

1. Run `dotnet --version` — confirm .NET 8.0+ is installed. If not, instruct the user to install [.NET 8.0 SDK](https://dotnet.microsoft.com/download).
2. Run `a365 --version` or `a365 -h` to check if the CLI is installed and up-to-date.

### Install or update

- **If not installed:** `dotnet tool install --global Microsoft.Agents.A365.DevTools.Cli --prerelease`
- **If outdated:** `dotnet tool update --global Microsoft.Agents.A365.DevTools.Cli --prerelease`
- **Windows alternative:** Run `scripts/cli/install-cli.ps1` from the repository (after `dotnet tool uninstall -g Microsoft.Agents.A365.DevTools.Cli` if needed).

### Verify

Run `a365 -h` — it should show usage information. If `a365` is not found, ensure `~/.dotnet/tools` is on PATH and restart the shell.

If a command referenced later (e.g., `publish`) is not recognized, upgrade the CLI. Always use the latest preview version.

> **Mark Todo 1 as completed. Mark Todo 2 as in-progress. Proceed to Step 2.**

---

## Step 2: Ensure Prerequisites and Environment Configuration

> Mark Todo 2 as **in-progress** now.

### Azure CLI & Authentication

1. Run `az --version` — if not installed, direct the user to install [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli).
2. Run `az login` (and `az account set -s <SubscriptionNameOrID>` if needed). The Agent 365 CLI uses this authentication context.

### Microsoft Entra ID roles

The logged-in user needs **Agent ID Administrator** or **Agent ID Developer** role. Full setup requires **Global Administrator + Azure Contributor**. If the user lacks these, prompt them to use an appropriate account or have an admin grant the roles.

### Custom client app validation

Ask the user: **"Please provide the Application (client) ID for your custom Agent 365 client app registration."**

If they don't have one, skip to "If validation fails" below.

Once provided, run this **exact command** (replace `<CLIENT_APP_ID>`):

```bash
az ad app show --id <CLIENT_APP_ID> --query "{appId:appId, displayName:displayName, requiredResourceAccess:requiredResourceAccess}" -o json && az ad app permission list-grants --id <CLIENT_APP_ID> --query "[].{resourceDisplayName:resourceDisplayName, scope:scope}" -o table
```

Verify these 5 **delegated** permissions appear with **admin consent granted**:

| Permission | Description |
|------------|-------------|
| `AgentIdentityBlueprint.ReadWrite.All` | Manage Agent 365 Blueprints |
| `AgentIdentityBlueprint.UpdateAuthProperties.All` | Update Blueprint auth properties |
| `Application.ReadWrite.All` | Create and manage Azure AD applications |
| `DelegatedPermissionGrant.ReadWrite.All` | Grant delegated permissions |
| `Directory.Read.All` | Read directory data |

**If validation fails** (app not found, permissions missing, or no admin consent):

1. STOP — do not proceed to any `a365` CLI commands.
2. Direct the user to the official setup guide: register the app as a **Public client** with redirect URI `http://localhost:8400`, add all five permissions, and have a Global Admin grant admin consent.
3. Wait for confirmation, then re-run the validation command.

Save the `clientAppId` — it will be used automatically in Step 3 (do NOT ask again).

### Validate language-specific build tools

Detect the project type and validate the required tools are installed:

| Project Type | Detected By | Validate | Minimum Version |
|-------------|-------------|----------|-----------------|
| **.NET** | `*.csproj` files | `dotnet --version` | 8.0+ |
| **Node.js** | `package.json` | `node --version && npm --version` | Node 18.x+ |
| **Python** | `requirements.txt` or `pyproject.toml` | `python --version && pip --version` | Python 3.10+ |

If tools are missing, instruct the user to install them before proceeding.

> **Step 2 completion — Before moving on, verify ALL passed:**
> - [ ] Azure CLI installed and logged in
> - [ ] Custom client app validated with all 5 permissions
> - [ ] Build tools installed for the detected project type
>
> **Mark Todo 2 as completed. Mark Todo 3 as in-progress. Proceed to Step 3.**

---

## Step 3: Configure the Agent 365 CLI (Initialize Configuration)

> Mark Todo 3 as **in-progress** now. Todos 1 and 2 must already be **completed**.

The `a365 config init` command is non-interactive, so you must create an `a365.config.json` file directly and then import it.

### Gather auto-detected values

```bash
az account show --query "{tenantId:tenantId, subscriptionId:id}" -o json
```

You already have `clientAppId` from Step 2. Set `deploymentProjectPath` to the current working directory (absolute path).

### Ask deployment type

Send the user **ONLY** this message, then **STOP and WAIT** for their reply:

---

**Do you want to create a web app in Azure for this agent? (yes/no)**

- **Yes** = Azure-hosted (recommended for production)
- **No** = Self-hosted (e.g., local development with dev tunnel)

---

> Do NOT show input fields, tables, or any other content with this question. WAIT for the user's response.

After the user responds: **yes** → `needDeployment: true` | **no** → `needDeployment: false`

### Collect configuration inputs

First, query real example values from the subscription (run as **one command**):

```bash
az ad signed-in-user show --query userPrincipalName -o tsv; az group list --query "[].{Name:name, Location:location}" -o table; az appservice plan list --query "[].{Name:name, ResourceGroup:resourceGroup, Location:location}" -o table
```

Extract: `{loggedInUser}`, `{existingResourceGroup}`, `{existingLocations}`, `{existingAppServicePlan}`. Use descriptive fallbacks (e.g., `my-agent-rg`) if queries return no results.

#### If Azure-hosted (`needDeployment: true`)

**"Please provide the following values to configure your Azure-hosted agent:"**

| Field | Description | Example |
|-------|-------------|---------|
| **Resource Group** | Azure Resource Group (new or existing) | `{existingResourceGroup}` |
| **Location** | Azure region for deployment | `{existingLocations}` |
| **Agent Name** | Unique name for your agent | `contoso-support-agent` |
| **Manager Email** | M365 manager email (from your tenant) | `{loggedInUser}` |
| **App Service Plan** | Azure App Service Plan name | `{existingAppServicePlan}` |

> **Agent Name rules:** Globally unique across Azure. Lowercase letters, numbers, hyphens only. Start with a letter. 3-20 chars. Tip: include your org name.
> **Do NOT ask for `clientAppId` here** — it was collected in Step 2.

#### If self-hosted (`needDeployment: false`)

**"Please provide the following values to configure your self-hosted agent:"**

| Field | Description | Example |
|-------|-------------|---------|
| **Agent Name** | Unique name for your agent | `contoso-support-agent` |
| **Manager Email** | M365 manager email (from your tenant) | `{loggedInUser}` |

Then ask: **"Would you like to use a dev tunnel for local development, or provide a custom messaging endpoint? (devtunnel/custom)"**
- **devtunnel**: The tunnel URL becomes the `messagingEndpoint`.
- **custom**: Ask the user for their `messagingEndpoint` URL.

### Derive naming values

Using `agentBaseName` and the domain from `managerEmail`:

| Field | Pattern | Example (`mya365agent`, `contoso.onmicrosoft.com`) |
|-------|---------|---------|
| `agentIdentityDisplayName` | `{baseName} Identity` | `mya365agent Identity` |
| `agentBlueprintDisplayName` | `{baseName} Blueprint` | `mya365agent Blueprint` |
| `agentUserPrincipalName` | `UPN.{baseName}@{domain}` | `UPN.mya365agent@contoso.onmicrosoft.com` |
| `agentUserDisplayName` | `{baseName} Agent User` | `mya365agent Agent User` |
| `agentDescription` | `{baseName} - Agent 365 Agent` | `mya365agent - Agent 365 Agent` |
| `webAppName` (Azure only) | `{baseName}-webapp` | `mya365agent-webapp` |

Present derived values and ask: **"Would you like to update any of these derived values, or proceed with the defaults? (update/proceed)"**

### Create the a365.config.json file

**Azure-hosted template** (`needDeployment: true`):

```json
{
  "tenantId": "<from az account show>",
  "subscriptionId": "<from az account show>",
  "resourceGroup": "<user provided>",
  "location": "<user provided>",
  "environment": "prod",
  "needDeployment": true,
  "clientAppId": "<from Step 2>",
  "appServicePlanName": "<user provided>",
  "webAppName": "<derived>",
  "agentIdentityDisplayName": "<derived>",
  "agentBlueprintDisplayName": "<derived>",
  "agentUserPrincipalName": "<derived>",
  "agentUserDisplayName": "<derived>",
  "managerEmail": "<user provided>",
  "agentUserUsageLocation": "US",
  "deploymentProjectPath": "<cwd>",
  "agentDescription": "<derived>"
}
```

**Self-hosted template** (`needDeployment: false`):

```json
{
  "tenantId": "<from az account show>",
  "subscriptionId": "<from az account show>",
  "resourceGroup": "<user provided>",
  "location": "<user provided>",
  "environment": "prod",
  "messagingEndpoint": "<user provided or dev tunnel URL>",
  "needDeployment": false,
  "clientAppId": "<from Step 2>",
  "agentIdentityDisplayName": "<derived>",
  "agentBlueprintDisplayName": "<derived>",
  "agentUserPrincipalName": "<derived>",
  "agentUserDisplayName": "<derived>",
  "managerEmail": "<user provided>",
  "agentUserUsageLocation": "US",
  "deploymentProjectPath": "<cwd>",
  "agentDescription": "<derived>"
}
```

### Import and validate

```bash
a365 config init -c ./a365.config.json
```

If validation fails (app not found, missing permissions, unrecognized project), correct `a365.config.json` and re-run. If it warns about project platform detection, verify `deploymentProjectPath`.

> **Mark Todo 3 as completed. Mark Todo 4 as in-progress. Proceed to Step 4.**

---

## Step 4: Run Agent 365 Setup to Provision Prerequisites

> Mark Todo 4 as **in-progress** now.

### Execute

```bash
a365 setup all
```

This provisions Azure infrastructure (Resource Group, App Service Plan, Web App, Managed Identity), creates the Agent 365 Blueprint in Entra ID, configures permissions, and registers the messaging endpoint. It may take a few minutes.

### Handle errors

| Error Pattern | Cause | Fix |
|--------------|-------|-----|
| Quota exceeded / "additional quota" | Azure subscription limit | Change region/SKU in config, re-run |
| "Region is not supported" | Unsupported Azure region | Update `location` in config, re-run |
| "Authorization_RequestDenied" / Forbidden | Insufficient Graph permissions | Re-validate client app (Step 2), have Global Admin grant consent |
| `InteractiveBrowserCredential` failed | Headless environment | Run in environment with browser, or use `--use-device-code` if available |
| Resource already exists | Previous partial run | Safe to re-run — CLI is idempotent |

If the CLI prints a consent URL, instruct the user (Global Admin) to open it and approve, then re-run.

You can re-run `a365 setup all` safely after fixing any issue. Use `a365 cleanup` only as a last resort.

> **Mark Todo 4 as completed. Mark Todo 5 as in-progress. Proceed to Step 5.**

---

## Step 5: Publish and Deploy the Agent Application

> Mark Todo 5 as **in-progress** now.

### Review the manifest file

Ask the user to review and update `manifest.json` in their project root. Key fields to customize:

| Field | What to Update |
|-------|----------------|
| `name.short` / `name.full` | Agent display name (max 30 / 100 chars) |
| `description.short` / `description.full` | What the agent does (max 80 / 4000 chars) |
| `developer.name` | Organization name |
| `developer.websiteUrl` | Organization website |
| `developer.privacyUrl` | Privacy policy URL (required for production) |
| `developer.termsOfUseUrl` | Terms of use URL (required for production) |
| `icons.color` / `icons.outline` | Color icon (192x192 PNG) and outline icon (32x32 PNG) |
| `accentColor` | Hex color for branding (e.g., `#0078D4`) |
| `version` | Semantic version (e.g., `1.0.0`) |

> The `id` and `agenticUserTemplates[].id` fields are auto-populated by the CLI — do not set manually.

Ask: **"Have you updated the manifest with your agent's name, description, and developer information? (yes/no)"** — wait for confirmation before proceeding.

### Publish

```bash
a365 publish
```

This updates manifest IDs and publishes the agent package to your tenant's Microsoft 365 catalog. If errors mention authorization or Graph failures, re-check the client app's `Application.ReadWrite.All` permission.

### Deploy (Azure-hosted only)

```bash
a365 deploy
```

This builds your project, deploys to the Azure Web App, and configures application settings. If the build fails, try building manually first (`dotnet build`, `npm run build`, etc.) to diagnose. For subsequent code-only deployments, use `a365 deploy app`.

### Post-deployment (User action required)

The following steps require browser-based interaction and must be completed by the user manually.

#### 1. Configure agent in Teams Developer Portal

1. Run `a365 config display -g` and copy the `agentBlueprintAppId` value.
2. Open: `https://dev.teams.microsoft.com/tools/agent-blueprint/<your-blueprint-app-id>/configuration`
3. Set **Agent Type** to `Bot Based`, **Bot ID** to the blueprint app ID, and click **Save**.

#### 2. Create agent instance

1. Open **Teams > Apps** and search for the agent name.
2. Click **Request Instance** (or **Create Instance**).
3. Admin approves at [Microsoft admin center - Requested Agents](https://admin.cloud.microsoft/#/agents/all/requested).

> The user must be in the [Frontier preview program](https://adoption.microsoft.com/copilot/frontier-program/) to create instances during preview.

#### 3. Test the agent

1. Search for the agent user in Teams (may take minutes to hours to appear).
2. Start a chat and send test messages (e.g., "Hello!").
3. Check logs for Azure-hosted: `az webapp log tail --name <web-app> --resource-group <rg>`
4. View in admin center: [Microsoft 365 admin center - Agents](https://admin.cloud.microsoft/#/agents/all)

> **Mark Todo 5 as completed. Setup is done.**

---

## Troubleshooting Quick Reference

### General

- Run failing commands with `-v` / `--verbose` for detailed logs.
- Check log files: Windows `%APPDATA%/a365/logs/`, Linux/Mac `~/.config/a365/logs/`.
- Search [GitHub issues](https://github.com/microsoft/Agent365-devTools/issues) by error message.
- Run `a365 <command> --help` for command-specific options.

### Common issues

| Issue | Solution |
|-------|----------|
| `a365` not found after install | Add `~/.dotnet/tools` to PATH, restart shell |
| NuGet package not found | Ensure `--prerelease` flag is included, check internet |
| "No subscription found" | Run `az login` and `az account set` |
| `InteractiveBrowserCredential` error | Use environment with browser, or `--use-device-code` |
| "Failed to acquire token" | Re-check client app permissions and admin consent |
| Build fails during deploy | Run build manually first (`dotnet build`, `npm run build`) |
| "Publish folder not found" | Run full `a365 deploy` (not `--restart`) at least once |
| Dev tunnel not receiving messages | Verify tunnel is running, port matches, `--allow-anonymous` was used |
| Dev tunnel URL changed | Run `a365 setup blueprint --update-endpoint <new-url>` |

### Escalating to GitHub

If the issue appears to be a CLI bug (not user error), draft an issue with:
- **Title:** Concise problem summary
- **Environment:** CLI version (`a365 --version`), OS, shell, Azure CLI version
- **Steps to reproduce:** Exact command sequence
- **Error output:** Relevant logs (redact sensitive GUIDs)
- **Expected vs actual behavior**

Present the draft to the user — do not create the issue unless authorized.

### Cleanup

Use `a365 cleanup` commands only as a last resort:
- `a365 cleanup azure` — removes Azure resources
- `a365 cleanup blueprint` — removes the Entra ID blueprint app

### References

- [Agent 365 CLI overview](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/agent-365-cli)
- [CLI command reference](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/agent-365-cli)
- [Custom client app setup](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/custom-engine-agent-setup)
- [Create agent instances](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/create-instance)
- [Troubleshooting guide](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/troubleshooting)
