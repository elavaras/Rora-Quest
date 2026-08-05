// Rora Quest — Azure Container Apps infrastructure
// Deploy with:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infra/aca/main.bicep \
//     --parameters @infra/aca/main.parameters.json

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Short environment tag: dev, staging, prod')
param environment string = 'prod'

@description('Container image tag to deploy (e.g. latest or a git sha)')
param imageTag string = 'latest'

@description('Entra app Client ID')
param entraClientId string

@description('Tenant mode — use "organizations" for multi-tenant work/school accounts')
param entraTenantId string = 'organizations'

@description('Name of the Azure Container Registry (existing)')
param acrName string

var storageAccountName = 'roraquest${toLower(environment)}assets'
var storageContainerName = 'task-assets'
var storageKeys = storage.listKeys()
var storageAccountConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageKeys.keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'

// ─────────────────────────────────────────────
// User-Assigned Managed Identity — ACR pull
// ─────────────────────────────────────────────
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'rora-quest-pull-id-${environment}'
  location: location
}

// Reference the existing ACR
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

// Grant AcrPull to the UAMI so Container Apps can pull images
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, pullIdentity.id, acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─────────────────────────────────────────────
// Key Vault — stores runtime secrets
// ─────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'rora-kv-${environment}'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ─────────────────────────────────────────────
// Log Analytics workspace (for Container Apps)
// ─────────────────────────────────────────────
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'rora-quest-logs-${environment}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ─────────────────────────────────────────────
// Blob storage for task assets
// ─────────────────────────────────────────────
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource storageConnStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'StorageConnectionString'
  properties: {
    value: storageAccountConnectionString
  }
}

// ─────────────────────────────────────────────
// Container Apps Environment
// ─────────────────────────────────────────────
resource caEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: 'rora-quest-env-${environment}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ─────────────────────────────────────────────
// API — Container App
// ─────────────────────────────────────────────
resource apiApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'rora-quest-api'
  location: location
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: caEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 5000
        transport: 'http'
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: pullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'kv-ref-entra-secret'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/EntraClientSecret'
          identity: 'system'
        }
        {
          name: 'kv-ref-pg-connstr'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/PostgresConnectionString'
          identity: 'system'
        }
        {
          name: 'kv-ref-storage-connstr'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/StorageConnectionString'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrName}.azurecr.io/rora-quest-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Postgres__MigrationsPath', value: '/app/migrations' }
            { name: 'EntraAuth__ClientId', value: entraClientId }
            { name: 'EntraAuth__TenantId', value: entraTenantId }
            { name: 'EntraAuth__CallbackPath', value: '/signin-oidc' }
            { name: 'EntraAuth__SignedOutCallbackPath', value: '/signout-callback-oidc' }
            { name: 'Cors__AllowedOrigins', value: 'https://${webApp.properties.configuration.ingress.fqdn}' }
            { name: 'Storage__ContainerName', value: storageContainerName }
            { name: 'Storage__ConnectionString', secretRef: 'kv-ref-storage-connstr' }
            { name: 'EntraAuth__ClientSecret', secretRef: 'kv-ref-entra-secret' }
            { name: 'ConnectionStrings__Postgres', secretRef: 'kv-ref-pg-connstr' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 5000, scheme: 'HTTP' }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 5000, scheme: 'HTTP' }
              initialDelaySeconds: 10
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaler'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
  dependsOn: [
    storageConnStringSecret
  ]
}

// ─────────────────────────────────────────────
// Web — Container App
// ─────────────────────────────────────────────
resource webApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'rora-quest-web'
  location: location
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: caEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 3000
        transport: 'http'
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: pullIdentity.id
        }
      ]
      secrets: []
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${acrName}.azurecr.io/rora-quest-web:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'NODE_ENV', value: 'production' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaler'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// ─────────────────────────────────────────────
// RBAC — grant API managed identity Key Vault Secrets User role
// ─────────────────────────────────────────────
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource apiKvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─────────────────────────────────────────────
// Outputs
// ─────────────────────────────────────────────
output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output pullIdentityId string = pullIdentity.id
