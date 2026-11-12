# Database migrations

TaxFlow uses EF Core migrations for schema changes. The existing database was
created originally with `EnsureCreated`; its current schema was registered as
`InitialSchemaBaseline` without recreating any tables. The follow-up migration
`AddOcrCorrections` creates the OCR correction history table.

## Normal startup

The backend runs `Database.MigrateAsync()` during startup. Pending migrations
are applied before the API starts serving requests.

## Create a backup

Run from the repository root before a production migration:

```sh
docker compose exec -T mysql sh -c 'mysqldump --single-transaction --no-tablespaces --routines --triggers -utaxflow -p"$MYSQL_PASSWORD" taxflow' > backups/taxflow-$(date +%Y%m%d-%H%M%S).sql
```

Backups are intentionally ignored by git because they contain business data.

## Create a migration

Use the .NET 8 SDK container when the host does not have the .NET 8 runtime:

```sh
docker run --rm -v "$PWD/backend:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0.422-bookworm-slim \
  bash -lc 'dotnet tool install --tool-path /tmp/ef dotnet-ef --version 8.0.30 && dotnet restore && /tmp/ef/dotnet-ef migrations add MigrationName --output-dir Data/Migrations'
```

Never drop the database or delete existing migration history in order to make a
migration pass.