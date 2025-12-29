# RulesApp Infrastructure Deployment Script
# Deploys Azure Storage Account for specified environment

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('local', 'dev', 'prod')]
    [string]$Environment,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroupName = "rg-rulesapp-$Environment",
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "canadacentral",
    
    [Parameter(Mandatory=$false)]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 RulesApp Infrastructure Deployment" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "Location: $Location" -ForegroundColor Yellow
Write-Host ""

# Check if Azure CLI is installed
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI is not installed. Please install from: https://aka.ms/azure-cli"
    exit 1
}

# Check if logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "⚠️  Not logged into Azure. Running 'az login'..." -ForegroundColor Yellow
    az login
    $account = az account show | ConvertFrom-Json
}

Write-Host "✅ Logged in as: $($account.user.name)" -ForegroundColor Green
Write-Host "   Subscription: $($account.name) ($($account.id))" -ForegroundColor Gray
Write-Host ""

# Create resource group if it doesn't exist
Write-Host "📦 Checking resource group..." -ForegroundColor Cyan
$rgExists = az group exists --name $ResourceGroupName | ConvertFrom-Json
if (-not $rgExists) {
    Write-Host "   Creating resource group: $ResourceGroupName" -ForegroundColor Yellow
    if (-not $WhatIf) {
        az group create --name $ResourceGroupName --location $Location --output none
        Write-Host "   ✅ Resource group created" -ForegroundColor Green
    } else {
        Write-Host "   [WhatIf] Would create resource group" -ForegroundColor Gray
    }
} else {
    Write-Host "   ✅ Resource group exists" -ForegroundColor Green
}
Write-Host ""

# Deploy Bicep template
Write-Host "🔨 Deploying infrastructure..." -ForegroundColor Cyan
$paramFile = Join-Path $PSScriptRoot "$Environment.bicepparam"
$templateFile = Join-Path $PSScriptRoot "main.bicep"

if (-not (Test-Path $paramFile)) {
    Write-Error "Parameter file not found: $paramFile"
    exit 1
}

if (-not (Test-Path $templateFile)) {
    Write-Error "Template file not found: $templateFile"
    exit 1
}

$deploymentName = "rulesapp-storage-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

if ($WhatIf) {
    Write-Host "   [WhatIf] Validating deployment..." -ForegroundColor Gray
    az deployment group what-if `
        --resource-group $ResourceGroupName `
        --template-file $templateFile `
        --parameters $paramFile `
        --name $deploymentName
} else {
    az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file $templateFile `
        --parameters $paramFile `
        --name $deploymentName `
        --output json | Out-Null
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Deployment completed successfully" -ForegroundColor Green
    } else {
        Write-Error "Deployment failed"
        exit 1
    }
}
Write-Host ""

# Get outputs
if (-not $WhatIf) {
    Write-Host "📋 Deployment Outputs:" -ForegroundColor Cyan
    $outputs = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query properties.outputs `
        --output json | ConvertFrom-Json
    
    Write-Host "   Storage Account Name: $($outputs.storageAccountName.value)" -ForegroundColor Yellow
    Write-Host "   Blob Endpoint: $($outputs.blobEndpoint.value)" -ForegroundColor Gray
    Write-Host "   Queue Endpoint: $($outputs.queueEndpoint.value)" -ForegroundColor Gray
    Write-Host "   Table Endpoint: $($outputs.tableEndpoint.value)" -ForegroundColor Gray
    Write-Host ""
    
    # Get connection string
    Write-Host "🔑 Connection String:" -ForegroundColor Cyan
    $connectionString = $outputs.storageAccountConnectionString.value
    Write-Host "   $connectionString" -ForegroundColor Yellow
    Write-Host ""
    
    # Save to environment-specific file
    $envFile = Join-Path $PSScriptRoot ".env.$Environment"
    @"
# RulesApp - $Environment Environment
# Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

AZURE_STORAGE_ACCOUNT_NAME=$($outputs.storageAccountName.value)
AZURE_STORAGE_CONNECTION_STRING=$connectionString
"@ | Out-File -FilePath $envFile -Encoding utf8 -Force
    
    Write-Host "✅ Connection string saved to: $envFile" -ForegroundColor Green
    Write-Host ""
    Write-Host "📝 Next Steps:" -ForegroundColor Cyan
    Write-Host "   1. Copy the connection string above" -ForegroundColor White
    Write-Host "   2. Update src/RulesApp.Api/local.settings.json:" -ForegroundColor White
    Write-Host '      "AzureWebJobsStorage": "<connection-string>"' -ForegroundColor Gray
    Write-Host ""
}
