// Parameter file for PROD environment
using './main.bicep'

param environmentName = 'prod'
param location = 'canadacentral' // Change to your preferred region
param storageSku = 'Standard_GRS' // Geo-redundant for production
param appName = 'rulesapp'
