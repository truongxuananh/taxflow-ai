# Backend completion notes

This pass focuses on data consistency and runtime stability with MySQL 8.4 + EF Core/Pomelo under Docker.

## Run

```powershell
docker compose down
docker compose up -d --build
docker compose ps
```

Do **not** use `docker compose down -v` unless you intentionally want to delete the MySQL volume and all project data.

## End-to-end smoke test

After all four services are healthy:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\backend-smoke.ps1
```

The script validates login, POS full payment, POS partial payment + receivable settlement, income/expense, dashboard/cashflow, tax draft idempotency, reports, reconciliation, AI endpoints, audit logs, and CSV export.

## Useful logs

```powershell
docker compose logs -f backend
docker compose logs --tail=200 backend
```

Backend: http://localhost:5001/swagger
Frontend: http://localhost:3000
AI: http://localhost:8000/health
MySQL from Windows host: localhost:3307
MySQL inside Docker: mysql:3306
