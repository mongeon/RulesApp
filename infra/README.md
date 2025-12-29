# RulesApp Infrastructure

Infrastructure as Code (IaC) for RulesApp using Azure Bicep.

## Overview

This folder contains Bicep templates and deployment scripts for managing Azure Storage accounts across three environments:
- **local** - Dedicated storage account for local development (replaces Azurite)
- **dev** - Development/staging environment
- **prod** - Production environment

Each environment gets its own Azure Storage Account with:
- **Blob containers**: `rules-pdfs`, `rules-pages`, `rules-chunks`, `rules-metadata`
- **Queues**: `rules-ingest`, `rules-ingest-poison`
- **Tables**: `IngestJobs`, `Seasons`, `Associations`

## Why Real Azure Storage for Local Dev?

We use real Azure Storage accounts (with a dedicated "local" environment) instead of Azurite because:
1. **Azurite has critical bugs**: Queue message retrieval fails, large blob downloads (>2.6MB) fail
2. **Consistency**: Local dev matches dev/prod behavior exactly
3. **Cost**: Local development storage costs are minimal (pennies per month)
4. **Reliability**: No emulator quirks or version-specific issues

## Prerequisites

- [Azure CLI](https://aka.ms/azure-cli) installed
- Azure subscription with permissions to create resources
- PowerShell 7+ (recommended) or PowerShell 5.1

## Quick Start

### 1. Deploy Local Environment

```powershell
# From the infra/ directory
.\deploy.ps1 -Environment local
```

This will:
- Create resource group `rg-rulesapp-local`
- Deploy storage account with all containers, queues, and tables
- Save connection string to `.env.local`

### 2. Update local.settings.json

Copy the connection string from the deployment output and update `src/RulesApp.Api/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=rulesapplocalxxx;AccountKey=...;EndpointSuffix=core.windows.net",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

### 3. Restart Azure Functions

```powershell
cd ..\src\RulesApp.Api
func start --port 7071
```

Your local development environment now uses real Azure Storage! 🎉

## Deployment Commands

### Full Deployment Script

```powershell
# Deploy specific environment
.\deploy.ps1 -Environment local
.\deploy.ps1 -Environment dev
.\deploy.ps1 -Environment prod

# Deploy to custom resource group
.\deploy.ps1 -Environment local -ResourceGroupName "my-custom-rg"

# Validate deployment without applying (WhatIf)
.\deploy.ps1 -Environment local -WhatIf
```

### Quick Shortcuts

```powershell
# Load helper functions
. .\shortcuts.ps1

# Deploy environments
Deploy-Local
Deploy-Dev
Deploy-Prod

# Get connection string
Get-ConnectionString -Environment local
Get-ConnectionString -Environment dev
```

## File Structure

```
infra/
├── main.bicep              # Main Bicep template (storage account definition)
├── local.bicepparam        # Parameters for local environment
├── dev.bicepparam          # Parameters for dev environment
├── prod.bicepparam         # Parameters for prod environment
├── deploy.ps1              # Main deployment script
├── shortcuts.ps1           # Helper functions for quick deployments
├── .env.local              # Generated: connection strings for local (git-ignored)
├── .env.dev                # Generated: connection strings for dev (git-ignored)
├── .env.prod               # Generated: connection strings for prod (git-ignored)
└── README.md               # This file
```

## Configuration

### Environment Parameters

Edit the `.bicepparam` files to customize each environment:

```bicep
// local.bicepparam
param environmentName = 'local'
param location = 'canadacentral'      // Your Azure region
param storageSku = 'Standard_LRS'     // Local dev: LRS (cheapest)
param appName = 'rulesapp'            // Prefix for resource names
```

**SKU Options:**
- `Standard_LRS` - Locally redundant (cheapest, use for local/dev)
- `Standard_GRS` - Geo-redundant (recommended for prod)
- `Standard_RAGRS` - Read-access geo-redundant
- `Premium_LRS` - Premium performance (expensive)

### Resource Naming

Storage account names are generated as:
```
{appName}{environmentName}{uniqueString}
```
Example: `rulesapplocalxyz123abc` (max 24 characters, lowercase alphanumeric)

## Costs

Approximate monthly costs for a "local" storage account with minimal usage:
- **Storage**: < $0.50 (storing a few PDFs and JSON files)
- **Transactions**: < $0.10 (ingest jobs, searches)
- **Total**: ~ **$1-2 per month**

Dev and prod environments will vary based on actual usage.

## Security

### Connection Strings

- **Never commit `.env.*` files** (already in `.gitignore`)
- Connection strings contain account keys with full access
- For production, consider using:
  - Managed Identity (Azure Functions can authenticate without keys)
  - Azure Key Vault for secrets management

### Access Control

Storage accounts are configured with:
- TLS 1.2 minimum
- HTTPS-only traffic
- No public blob access
- Shared key access enabled (required for Functions)

## Troubleshooting

### Deployment Fails: "Storage account name already taken"

Storage account names must be globally unique. The template uses `uniqueString(resourceGroup().id)` to generate unique suffixes, but if you reuse resource groups, you may get conflicts.

**Solution**: Delete the old storage account or use a different resource group name.

### Can't Connect from Local Functions

1. **Check connection string**: Verify `local.settings.json` has the correct connection string from `.env.local`
2. **Restart Functions host**: Stop (`Ctrl+C`) and restart `func start`
3. **Test connectivity**:
   ```powershell
   # Test with Azure CLI
   az storage account show --name <storage-account-name>
   ```

### "Not logged into Azure"

```powershell
az login
# Or for specific subscription
az account set --subscription "<subscription-id>"
```

## Clean Up

### Delete Environment Resources

```powershell
# Delete entire resource group (removes all resources)
az group delete --name rg-rulesapp-local --yes --no-wait
az group delete --name rg-rulesapp-dev --yes --no-wait
az group delete --name rg-rulesapp-prod --yes --no-wait
```

**Warning**: This permanently deletes all data. Export important data first.

## Alternative: Terraform

If you prefer Terraform over Bicep, here's the equivalent structure:

```hcl
# main.tf
resource "azurerm_storage_account" "rulesapp" {
  name                     = "${var.app_name}${var.environment}${random_string.suffix.result}"
  resource_group_name      = azurerm_resource_group.rulesapp.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = var.storage_replication
  
  # ... (containers, queues, tables)
}
```

Let me know if you need a full Terraform conversion.

## Alternative: .NET Aspire

For .NET Aspire integration:

```csharp
// AppHost/Program.cs
var storage = builder.AddAzureStorage("storage")
    .AddBlobs("blobs")
    .AddQueues("queues")
    .AddTables("tables");

builder.AddProject<Projects.RulesApp_Api>("api")
    .WithReference(storage);
```

Aspire can provision resources but still requires Azure subscriptions. Let me know if you want Aspire setup.

## Next Steps

1. **Deploy local environment**: `.\deploy.ps1 -Environment local`
2. **Update local.settings.json** with connection string
3. **Test ingestion**: Upload a PDF via `/api/admin/upload` endpoint
4. **Monitor in Azure Portal**: View blobs, queues, tables in real-time

## Support

For issues or questions:
- Check Azure deployment logs: `az deployment group show --name <deployment-name> --resource-group <rg-name>`
- Review Bicep template: `main.bicep` has detailed comments
- Azure Storage docs: https://learn.microsoft.com/azure/storage/

---

**Ready to deploy?** Run `.\deploy.ps1 -Environment local` to get started! 🚀
