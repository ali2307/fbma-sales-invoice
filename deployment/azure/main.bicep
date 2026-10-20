@description('Globally unique Azure App Service name.')
param appName string

@description('Azure region for the App Service resources.')
param location string = resourceGroup().location

@description('Complete encrypted SQL Server connection string. Use the exact on-premises DNS host configured in Hybrid Connections.')
@secure()
param sqlConnectionString string

@secure()
param apiKey string

@secure()
param adminPassword string

@secure()
param smtpPassword string

param adminUsername string = 'admin'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ConnectionStrings__SalesDatabase', value: sqlConnectionString }
        { name: 'Security__ApiKey', value: apiKey }
        { name: 'Security__AdminUsername', value: adminUsername }
        { name: 'Security__AdminPassword', value: adminPassword }
        { name: 'Smtp__Password', value: smtpPassword }
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      ]
    }
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}'
