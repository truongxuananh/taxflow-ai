$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Check-Exit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed." } }
npm --prefix frontend ci; Check-Exit 'npm ci'
npm --prefix frontend run build; Check-Exit 'Frontend build'
npm --prefix frontend test; Check-Exit 'Frontend DOM tests'
dotnet test TaxFlow.sln --logger 'trx;LogFileName=local.trx'; Check-Exit '.NET tests'
Push-Location ai-service
try { python -m pytest -q; Check-Exit 'Python tests' } finally { Pop-Location }
docker compose config --quiet; Check-Exit 'Compose config'
Write-Host 'All available validation steps completed.'
