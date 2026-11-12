param([Parameter(Mandatory=$true)][string]$SqlFile)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (!(Test-Path -LiteralPath $SqlFile -PathType Leaf)) { throw 'SQL file not found.' }
Write-Host 'This overwrites the current database. Back it up first and verify the target in .env.' -ForegroundColor Yellow
$confirm = Read-Host 'Type RESTORE to continue'
if ($confirm -cne 'RESTORE') { throw 'Restore cancelled.' }
Get-Content -LiteralPath $SqlFile -Encoding UTF8 | & docker compose exec -T mysql sh -c 'exec mysql --default-character-set=utf8mb4 -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DATABASE"'
if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Do not assume the database is complete.' }
Write-Host 'Restore completed. Restore the matching storage backup separately, then restart backend.'
