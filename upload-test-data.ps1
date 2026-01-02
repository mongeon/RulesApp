# upload-test-data.ps1
# Upload all test rulebook PDFs to RulesApp

$downloadPath = "C:\Users\gabri\Downloads"
$uploadScript = Join-Path $PSScriptRoot "upload-pdf.ps1"

Write-Host "================================================" -ForegroundColor Yellow
Write-Host "  RulesApp Test Data Upload" -ForegroundColor Yellow
Write-Host "================================================" -ForegroundColor Yellow
Write-Host ""

# Canada French
Write-Host "[1/5] Uploading Canada French..." -ForegroundColor Cyan
& $uploadScript -FilePath "$downloadPath\6cb071ff-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2025" -ScopeLevel "Canada" -DocType "Fr"
Write-Host ""

# Canada English
Write-Host "[2/5] Uploading Canada English..." -ForegroundColor Cyan
& $uploadScript -FilePath "$downloadPath\6cb07278-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2025" -ScopeLevel "Canada" -DocType "En"
Write-Host ""

# Québec French
Write-Host "[3/5] Uploading Québec French..." -ForegroundColor Cyan
& $uploadScript -FilePath "$downloadPath\6ccc88f7-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2025" -ScopeLevel "Quebec" -DocType "Fr"
Write-Host ""

# Québec English
Write-Host "[4/5] Uploading Québec English..." -ForegroundColor Cyan
& $uploadScript -FilePath "$downloadPath\6ccc8805-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2025" -ScopeLevel "Quebec" -DocType "En"
Write-Host ""

# Baseball Laurentides
Write-Host "[5/5] Uploading Baseball Laurentides (Regional)..." -ForegroundColor Cyan
& $uploadScript -FilePath "$downloadPath\6c755337-47b6-11f0-a918-0614b4bc81d9.pdf" `
    -SeasonId "2025" -ScopeLevel "Regional" -DocType "Fr" -AssociationId "LBML"
Write-Host ""

Write-Host "================================================" -ForegroundColor Green
Write-Host "  All uploads complete!" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Check latest job: curl.exe http://localhost:7071/api/admin/jobs/latest" -ForegroundColor Gray
Write-Host "2. View chunks: curl.exe http://localhost:7071/api/admin/debug/chunks?jobId=<jobId>" -ForegroundColor Gray
