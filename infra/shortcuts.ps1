# Quick deployment shortcuts for RulesApp infrastructure

# Deploy LOCAL environment
function Deploy-Local {
    & "$PSScriptRoot\deploy.ps1" -Environment local
}

# Deploy DEV environment
function Deploy-Dev {
    & "$PSScriptRoot\deploy.ps1" -Environment dev
}

# Deploy PROD environment
function Deploy-Prod {
    & "$PSScriptRoot\deploy.ps1" -Environment prod
}

# Validate deployment (WhatIf)
function Test-Deployment {
    param([string]$Environment = 'local')
    & "$PSScriptRoot\deploy.ps1" -Environment $Environment -WhatIf
}

# Get connection string for environment
function Get-ConnectionString {
    param(
        [ValidateSet('local', 'dev', 'prod')]
        [string]$Environment = 'local'
    )
    
    $envFile = Join-Path $PSScriptRoot ".env.$Environment"
    if (Test-Path $envFile) {
        Get-Content $envFile | Where-Object { $_ -match '^AZURE_STORAGE_CONNECTION_STRING=' } | ForEach-Object {
            $_.Replace('AZURE_STORAGE_CONNECTION_STRING=', '')
        }
    } else {
        Write-Warning "No connection string file found for $Environment environment. Run Deploy-$Environment first."
    }
}

Write-Host "RulesApp Infrastructure Commands Loaded:" -ForegroundColor Cyan
Write-Host "  Deploy-Local    - Deploy local environment" -ForegroundColor Yellow
Write-Host "  Deploy-Dev      - Deploy dev environment" -ForegroundColor Yellow
Write-Host "  Deploy-Prod     - Deploy prod environment" -ForegroundColor Yellow
Write-Host "  Test-Deployment - Validate deployment (WhatIf)" -ForegroundColor Yellow
Write-Host "  Get-ConnectionString -Environment <env> - Get connection string" -ForegroundColor Yellow
