param([string]$Destination = "backups")
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$path = Join-Path $Destination "taxflow-$stamp.sql"
$lines = & docker compose exec -T mysql sh -c 'exec mysqldump --single-transaction --no-tablespaces -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DATABASE"'
if ($LASTEXITCODE -ne 0) { throw 'Database backup failed; no backup marked successful.' }
[IO.File]::WriteAllText((Join-Path (Get-Location) $path), ($lines -join "`n"), (New-Object Text.UTF8Encoding $false))
if (Test-Path storage) { Compress-Archive -Path storage -DestinationPath (Join-Path $Destination "storage-$stamp.zip") -Force }
Get-FileHash -Algorithm SHA256 $path | Format-List
Write-Host "Saved database: $path. Keep a separate protected copy of .env; do not share it."
