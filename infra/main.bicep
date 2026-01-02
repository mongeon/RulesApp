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

@description('Deploy Azure OpenAI (optional, for chat AI enhancement)')
param deployOpenAI bool = false

@description('Azure OpenAI SKU')
@allowed([
  'S0'
])
param openAiSku string = 'S0'

@description('Azure OpenAI model deployment name')
param openAiDeploymentName string = 'gpt-4o-mini'

@description('Azure OpenAI location (must support OpenAI)')
param openAiLocation string = 'eastus2'

@description('Azure OpenAI model name')
param openAiModelName string = 'gpt-4o-mini'

@description('Azure OpenAI model version')
param openAiModelVersion string = '2024-07-18'

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
  'IngestionJobs'
  'SeasonState'
  'OverrideMappings'
  'Seasons'
  'Associations'
]: {
  parent: tableService
  name: tableName
  properties: {}
}]

// Azure AI Search Service
resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: '${appName}-search-${environmentName}-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: environmentName == 'prod' ? 'standard' : 'basic'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
  }
  tags: {
    Environment: environmentName
    Application: 'RulesApp'
  }
}

// Azure OpenAI Service (optional)
resource openAiService 'Microsoft.CognitiveServices/accounts@2023-05-01' = if (deployOpenAI) {
  name: '${appName}-openai-${environmentName}-${uniqueString(resourceGroup().id)}'
  location: openAiLocation
  kind: 'OpenAI'
  sku: {
    name: openAiSku
  }
  properties: {
    customSubDomainName: '${appName}-openai-${environmentName}-${uniqueString(resourceGroup().id)}'
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    Environment: environmentName
    Application: 'RulesApp'
  }
}

// Azure OpenAI Model Deployment (optional)
resource openAiDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = if (deployOpenAI) {
  parent: openAiService
  name: openAiDeploymentName
  sku: {
    name: 'Standard'
    capacity: 20
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: openAiModelName
      version: openAiModelVersion
    }
  }
}

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

@description('Search service name')
output searchServiceName string = searchService.name

@description('Search service endpoint')
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'

@description('Search service admin key')
output searchAdminKey string = searchService.listAdminKeys().primaryKey

@description('Azure OpenAI service name (if deployed)')
output openAiServiceName string = deployOpenAI ? openAiService.name : ''

@description('Azure OpenAI endpoint (if deployed)')
output openAiEndpoint string = deployOpenAI ? openAiService.properties.endpoint : ''

@description('Azure OpenAI key (if deployed)')
output openAiKey string = deployOpenAI ? openAiService.listKeys().key1 : ''

@description('Azure OpenAI deployment name (if deployed)')
output openAiDeploymentName string = deployOpenAI ? openAiDeployment.name : ''
