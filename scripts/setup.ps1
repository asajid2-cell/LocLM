# Setup script for Windows
Write-Host "Setting up LocLM..." -ForegroundColor Cyan

# Restore .NET dependencies
Write-Host "`nRestoring .NET packages..." -ForegroundColor Yellow
dotnet restore

# Setup Python
Write-Host "`nSetting up Python environment..." -ForegroundColor Yellow
Push-Location src\LocLM\backend
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt

# Generate tools
Write-Host "`nGenerating MCP tool stubs..." -ForegroundColor Yellow
python mcp_transpiler.py
Pop-Location

Write-Host "`nSetup complete!" -ForegroundColor Green
Write-Host "Run 'dotnet run --project src/LocLM' to start"
