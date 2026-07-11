@description('The name of the Static Web App')
param staticWebAppName string = 'nerr-bookshelfwallpaper'

@description('The location for all resources')
param location string = resourceGroup().location

@description('The Cosmos DB account name')
param cosmosAccountName string = 'nerr-bookshelf-cosmos-nore'

@description('The Storage Account name')
param storageAccountName string = 'nerr-bookshelf-sa-nore'

@description('The App Service Plan name for Azure Functions')
param functionAppPlanName string = 'nerr-bookshelf-plan-nore'

@description('The Azure Function App name')
param functionAppName string = 'nerr-bookshelf-api-func-nore'

@description('The Application Insights name')
param appInsightsName string = 'nerr-bookshelf-insights'

// ── Static Web App ─────────────────────────────────────────────────────────
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
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
  name: cosmosAccountName
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
  name: 'nerr-bookshelfWallpaper'
  parent: cosmosAccount
  properties: {
    resource: {
      id: 'nerr-bookshelfWallpaper'
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
  name: storageAccountName
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
  name: appInsightsName
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
