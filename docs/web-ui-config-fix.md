# Web UI Configuration Fix

## Changes Made

1. **Updated Program.cs** to read API base address from configuration
   - Default: `http://localhost:7071`
   - Configurable via `appsettings.json`

2. **Added Configuration Files**:
   - `wwwroot/appsettings.json` - Base configuration
   - `wwwroot/appsettings.Development.json` - Development-specific settings

3. **Added CORS Support to Functions API**:
   - Added `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` NuGet package
   - Configured CORS in `Program.cs` to allow requests from `https://localhost:7256`
   - The AspNetCore integration enables proper CORS handling for Blazor WebAssembly

## Testing

### 1. Stop any running instances
```powershell
Get-Process "func" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match "RulesApp.Web" } | Stop-Process -Force
```

### 2. Start the Functions API
```powershell
cd src\RulesApp.Api
func start
```

Wait for the message: **"Job host started"** and you see the Search endpoint listed.

### 3. Start the Web UI (in a new terminal)
```powershell
cd src\RulesApp.Web
dotnet run
```

Wait for: **"Now listening on: https://localhost:7256"**

### 4. Test the Search UI

1. Navigate to https://localhost:7256
2. Click "Search" in the navigation menu
3. Enter a query like "terrain de jeu"
4. Select scopes (Canada/Quebec/Regional)
5. Click "Search"
6. You should see results displayed

### 5. Verify in Browser Dev Tools

- Open F12 Developer Tools
- Go to Network tab
- Perform a search
- Look for POST request to `http://localhost:7071/api/search`
- **Status should be 200 OK** (not 405 or CORS error)
- Response should contain search results

## Expected Results

✅ No more CORS errors
✅ Search requests successfully POST to `http://localhost:7071/api/search`
✅ Results display in Web UI with proper formatting
✅ All filter options working (scopes, association ID)

## Configuration for Production

### Web UI (appsettings.json)
Update the API base URL to point to your deployed Functions API:
```json
{
  "ApiBaseAddress": "https://your-function-app.azurewebsites.net"
}
```

### Functions API (CORS)
Update CORS in [Program.cs](../src/RulesApp.Api/Program.cs):
```csharp
policy.WithOrigins("https://your-staticwebapp.azurestaticapps.net")
```

Or configure CORS in Azure Portal:
- Function App → API → CORS → Add your Static Web App URL

## Troubleshooting

### Still getting CORS errors?
- Verify both apps are running (check terminal output)
- Ensure appsettings.json has correct API URL
- Check browser console for actual error message
- Try a hard refresh (Ctrl+F5)

### 404 on search endpoint?
- Verify func start completed successfully
- Check that `/api/search` endpoint is listed in function list
- Test directly: `Invoke-RestMethod -Uri "http://localhost:7071/api/search" -Method Post -ContentType "application/json" -Body '{"query":"test"}'`

### Results not displaying?
- Check browser console for JavaScript errors
- Verify search response in Network tab
- Ensure SearchResponse JSON structure matches C# model
