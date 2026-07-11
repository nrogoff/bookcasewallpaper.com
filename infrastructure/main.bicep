@description('Environment short code used in resource names (dev, qa, sit, uat, stag, pre, prod)')
param environmentCode string = 'prod'

@description('Region short code used in resource names (ne, uks, we, nore, eus, sc)')
param regionCode string = 'sc'

@description('Optional override for Static Web App name. Leave empty to use naming convention.')
param staticWebAppName string = ''

@description('The location for all resources')
param location string = resourceGroup().location

@description('Optional override for Cosmos DB account name. Leave empty to use naming convention.')
param cosmosAccountName string = ''

@description('Optional override for Storage Account name. Leave empty to use naming convention.')
param storageAccountName string = ''

@description('Optional override for Function App plan name. Leave empty to use naming convention.')
param functionAppPlanName string = ''

@description('Optional override for Function App name. Leave empty to use naming convention.')
param functionAppName string = ''

@description('Optional override for Application Insights name. Leave empty to use naming convention.')
param appInsightsName string = ''

var orgCode = 'nerr'
var projectCode = 'bcwp'
var staticWebAppNameResolved = empty(staticWebAppName) ? '${orgCode}-${projectCode}-stapp-web-${environmentCode}-we' : staticWebAppName
var cosmosAccountNameResolved = empty(cosmosAccountName) ? '${orgCode}-${projectCode}-cosmos-data-${environmentCode}-${regionCode}' : cosmosAccountName
var storageAccountNameResolved = empty(storageAccountName) ? '${orgCode}${projectCode}stimg${environmentCode}${regionCode}' : storageAccountName
var functionAppPlanNameResolved = empty(functionAppPlanName) ? '${orgCode}-${projectCode}-asp-api-${environmentCode}-${regionCode}' : functionAppPlanName
var functionAppNameResolved = empty(functionAppName) ? '${orgCode}-${projectCode}-func-api-${environmentCode}-${regionCode}' : functionAppName
var appInsightsNameResolved = empty(appInsightsName) ? '${orgCode}-${projectCode}-appi-obs-${environmentCode}-${regionCode}' : appInsightsName
var cosmosDatabaseNameResolved = '${orgCode}-${projectCode}-cosmos-data-${environmentCode}-${regionCode}'

// ── Static Web App ─────────────────────────────────────────────────────────
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppNameResolved
  location: 'westeurope'
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    buildProperties: {
      appLocation: 'frontend'
      apiLocation: 'api'
      outputLocation: 'dist'
    }
  }
}

// ── Cosmos DB ──────────────────────────────────────────────────────────────
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountNameResolved
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    databaseAccountOfferType: 'Standard'
    enableAutomaticFailover: false
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  name: cosmosDatabaseNameResolved
  parent: cosmosAccount
  properties: {
    resource: {
      id: cosmosDatabaseNameResolved
    }
  }
}

resource bookshelvesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  name: 'bookshelves'
  parent: cosmosDatabase
  properties: {
    resource: {
      id: 'bookshelves'
      partitionKey: {
        paths: ['/userId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
  }
}

resource coverJobsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  name: 'coverFetchJobs'
  parent: cosmosDatabase
  properties: {
    resource: {
      id: 'coverFetchJobs'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
      }
      defaultTtl: 604800 // 7 days — completed jobs auto-delete
    }
  }
}

// ── Storage Account ────────────────────────────────────────────────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountNameResolved
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: true // Required for serving book cover images publicly
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

resource bookCoversContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'book-covers'
  parent: blobService
  properties: {
    publicAccess: 'Blob'
  }
}

resource wallpapersContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'wallpapers'
  parent: blobService
  properties: {
    publicAccess: 'Blob'
  }
}

// ── Application Insights ───────────────────────────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsNameResolved
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    RetentionInDays: 30
  }
}

// ── Outputs ────────────────────────────────────────────────────────────────
output staticWebAppUrl string = staticWebApp.properties.defaultHostname
output cosmosEndpoint string = cosmosAccount.properties.documentEndpoint
output storageAccountName string = storageAccount.name
output appInsightsKey string = appInsights.properties.InstrumentationKey
