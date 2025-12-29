// RulesApp Infrastructure - Main Template
// Creates Azure Storage Account with containers, queues, and tables

@description('Environment name (local, dev, prod)')
param environmentName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Storage account SKU')
@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_RAGRS'
  'Premium_LRS'
])
param storageSku string = 'Standard_LRS'

@description('Application name prefix')
param appName string = 'rulesapp'

// Generate unique storage account name (3-24 chars, lowercase alphanumeric only)
var storageAccountName = toLower('${appName}${environmentName}${uniqueString(resourceGroup().id)}')

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: storageSku
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
  tags: {
    Environment: environmentName
    Application: 'RulesApp'
  }
}

// Blob Service
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: environmentName == 'prod' ? 30 : 7
    }
  }
}

// Blob Containers
resource containersDefinition 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for containerName in [
  'rules-pdfs'
  'rules-pages'
  'rules-chunks'
  'rules-metadata'
]: {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}]

// Queue Service
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {}
}

// Queues
resource queuesDefinition 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = [for queueName in [
  'rules-ingest'
  'rules-ingest-poison'
]: {
  parent: queueService
  name: queueName
  properties: {
    metadata: {}
  }
}]

// Table Service
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {}
}

// Tables
resource tablesDefinition 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = [for tableName in [
  'IngestJobs'
  'Seasons'
  'Associations'
]: {
  parent: tableService
  name: tableName
  properties: {}
}]

// Outputs
@description('Storage account name')
output storageAccountName string = storageAccount.name

@description('Storage account primary connection string')
output storageAccountConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

@description('Storage account blob endpoint')
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob

@description('Storage account queue endpoint')
output queueEndpoint string = storageAccount.properties.primaryEndpoints.queue

@description('Storage account table endpoint')
output tableEndpoint string = storageAccount.properties.primaryEndpoints.table
