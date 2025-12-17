// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.DevTools.Cli.Constants;

/// <summary>
/// Constants for Microsoft Office Store (MOS) API authentication and permissions
/// </summary>
public static class MosConstants
{
    /// <summary>
    /// Redirect URI for MOS token acquisition (aligns with custom client app configuration)
    /// </summary>
    public const string RedirectUri = "http://localhost:8400/";

    /// <summary>
    /// Authority URL for commercial cloud MOS authentication
    /// </summary>
    public const string CommercialAuthority = "https://login.microsoftonline.com/common";

    /// <summary>
    /// Authority URL for US Government cloud MOS authentication (GCCH/DOD)
    /// </summary>
    public const string GovernmentAuthority = "https://login.microsoftonline.us/common";

    /// <summary>
    /// TPS AppServices 3p App (Client) - Microsoft first-party client app ID
    /// Used for MOS token acquisition (NOT the custom client app)
    /// </summary>
    public const string TpsAppServicesClientAppId = "caef0b02-8d39-46ab-b28c-f517033d8a21";

    /// <summary>
    /// TPS AppServices 3p App (Server) resource app ID
    /// Required for test environment token acquisition
    /// </summary>
    public const string TpsAppServicesResourceAppId = "6ec511af-06dc-4fe2-b493-63a37bc397b1";

    /// <summary>
    /// Power Platform API resource app ID for MOS token
    /// </summary>
    public const string PowerPlatformApiResourceAppId = "8578e004-a5c6-46e7-913e-12f58912df43";

    /// <summary>
    /// MOS Titles API resource app ID
    /// Required for accessing MOS Titles service (titles.prod.mos.microsoft.com)
    /// </summary>
    public const string MosTitlesApiResourceAppId = "e8be65d6-d430-4289-a665-51bf2a194bda";

    /// <summary>
    /// All MOS resource app IDs that need service principals created in the tenant
    /// </summary>
    public static readonly string[] AllResourceAppIds = new[]
    {
        TpsAppServicesResourceAppId,
        PowerPlatformApiResourceAppId,
        MosTitlesApiResourceAppId
    };

    /// <summary>
    /// Delegated permission scope IDs for MOS resource applications.
    /// These IDs are stable Microsoft-defined GUIDs that identify specific OAuth2 permissions.
    /// To verify these IDs are current, use Graph API or Azure Portal:
    /// - Graph API: GET https://graph.microsoft.com/v1.0/servicePrincipals(appId='{resourceAppId}')?$select=oauth2PermissionScopes
    /// - Azure Portal: Enterprise Applications > Search by App ID > Permissions
    /// </summary>
    public static class Permissions
    {
        /// <summary>
        /// TPS AppServices 3p App (Server) - AuthConfig.Read permission
        /// Allows reading authentication configuration
        /// Scope ID: 6f17ed22-2455-4cfc-a02d-9ccdde5f7f8c
        /// </summary>
        public static readonly (string Name, string Id) TpsAppServicesAuthConfigRead = 
            ("AuthConfig.Read", "6f17ed22-2455-4cfc-a02d-9ccdde5f7f8c");

        /// <summary>
        /// Power Platform API - EnvironmentManagement.Environments.Read permission
        /// Allows reading Power Platform environments
        /// Scope ID: 177690ed-85f1-41d9-8dbf-2716e60ff46a
        /// </summary>
        public static readonly (string Name, string Id) PowerPlatformEnvironmentsRead = 
            ("EnvironmentManagement.Environments.Read", "177690ed-85f1-41d9-8dbf-2716e60ff46a");

        /// <summary>
        /// MOS Titles API - Title.ReadWrite.All permission
        /// Allows reading and writing MOS titles (required for package upload)
        /// Scope ID: ecb8a615-f488-4c95-9efe-cb0142fc07dd
        /// </summary>
        public static readonly (string Name, string Id) MosTitlesTitleReadWriteAll = 
            ("Title.ReadWrite.All", "ecb8a615-f488-4c95-9efe-cb0142fc07dd");
    }

    /// <summary>
    /// Complete permission configuration for each MOS resource app.
    /// Each entry contains: scope name for OAuth2 grants, scope ID for requiredResourceAccess, and resource app ID.
    /// This centralized mapping ensures consistency between requiredResourceAccess configuration and OAuth2 permission grants.
    /// </summary>
    public static class ResourcePermissions
    {
        /// <summary>
        /// Permission configuration for TPS AppServices resource app.
        /// Required for test environment MOS operations.
        /// </summary>
        public static readonly (string ResourceAppId, string ScopeName, string ScopeId) TpsAppServices = 
            (TpsAppServicesResourceAppId, "AuthConfig.Read", "6f17ed22-2455-4cfc-a02d-9ccdde5f7f8c");

        /// <summary>
        /// Permission configuration for Power Platform API resource app.
        /// Required for environment management operations.
        /// </summary>
        public static readonly (string ResourceAppId, string ScopeName, string ScopeId) PowerPlatformApi = 
            (PowerPlatformApiResourceAppId, "EnvironmentManagement.Environments.Read", "177690ed-85f1-41d9-8dbf-2716e60ff46a");

        /// <summary>
        /// Permission configuration for MOS Titles API resource app.
        /// Requires multiple scopes for full publish functionality.
        /// </summary>
        public static readonly (string ResourceAppId, string ScopeName, string ScopeId) MosTitlesApi = 
            (MosTitlesApiResourceAppId, "AuthConfig.Read Title.ReadWrite Title.ReadWrite.All", "ecb8a615-f488-4c95-9efe-cb0142fc07dd");

        /// <summary>
        /// Gets all resource permission configurations.
        /// Use this to iterate over all MOS resource apps during setup.
        /// </summary>
        public static IEnumerable<(string ResourceAppId, string ScopeName, string ScopeId)> GetAll()
        {
            yield return TpsAppServices;
            yield return PowerPlatformApi;
            yield return MosTitlesApi;
        }
    }

    /// <summary>
    /// MOS environment configuration mapping
    /// Maps environment names to their scope URLs
    /// </summary>
    public static class Environments
    {
        public const string Prod = "prod";
        public const string Sdf = "sdf";
        public const string Test = "test";
        public const string Gccm = "gccm";
        public const string Gcch = "gcch";
        public const string Dod = "dod";

        /// <summary>
        /// Scope for production MOS environment
        /// </summary>
        public const string ProdScope = "https://titles.prod.mos.microsoft.com/.default";

        /// <summary>
        /// Scope for SDF MOS environment
        /// </summary>
        public const string SdfScope = "https://titles.sdf.mos.microsoft.com/.default";

        /// <summary>
        /// Scope for test MOS environment
        /// </summary>
        public const string TestScope = "https://testappservices.mos.microsoft.com/.default";

        /// <summary>
        /// Scope for GCCM MOS environment
        /// </summary>
        public const string GccmScope = "https://titles.gccm.mos.microsoft.com/.default";

        /// <summary>
        /// Scope for GCCH MOS environment
        /// </summary>
        public const string GcchScope = "https://titles.gcch.mos.svc.usgovcloud.microsoft/.default";

        /// <summary>
        /// Scope for DOD MOS environment
        /// </summary>
        public const string DodScope = "https://titles.dod.mos.svc.usgovcloud.microsoft/.default";
    }

    /// <summary>
    /// Generates Azure Portal URL for API permissions page of a specific client app
    /// </summary>
    public static string GetApiPermissionsPortalUrl(string clientAppId)
    {
        return $"https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/{clientAppId}";
    }
}
