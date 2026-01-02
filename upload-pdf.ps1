# upload-pdf.ps1
# Helper script to upload PDF files via JSON API

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [Parameter(Mandatory=$true)]
    [string]$ScopeLevel,  # Canada, Quebec, or Regional
    
    [Parameter(Mandatory=$true)]
    [string]$DocType,  # Fr or En
    
    [string]$SeasonId = "2026",
    
    [string]$AssociationId,
    
    [string]$ApiUrl = "http://localhost:7071/api/admin/upload"
)

# Check if file exists
if (-not (Test-Path $FilePath)) {
    Write-Error "File not found: $FilePath"
    exit 1
}

# Read file and convert to base64
Write-Host "Reading file: $FilePath" -ForegroundColor Cyan
$fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
$base64 = [Convert]::ToBase64String($fileBytes)
$fileName = [System.IO.Path]::GetFileName($FilePath)

Write-Host "File size: $($fileBytes.Length) bytes" -ForegroundColor Gray
Write-Host "Base64 size: $($base64.Length) characters" -ForegroundColor Gray

# Build JSON request
$body = @{
    seasonId = $SeasonId
    scopeLevel = $ScopeLevel
    docType = $DocType
    fileName = $fileName
    fileContentBase64 = $base64
}

if ($AssociationId) {
    $body.associationId = $AssociationId
}

$jsonBody = $body | ConvertTo-Json -Depth 10

# Upload
Write-Host "Uploading to $ApiUrl..." -ForegroundColor Cyan
try {
    $response = Invoke-RestMethod -Uri $ApiUrl -Method Post -Body $jsonBody -ContentType "application/json"
    Write-Host "✓ Upload successful!" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10 | Write-Host
}
catch {
    Write-Error "Upload failed: $_"
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message -ForegroundColor Red
    }
    exit 1
}
