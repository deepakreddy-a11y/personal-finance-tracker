# Personal Finance Tracker (Hackathon Starter)

This workspace contains the Personal Finance Tracker hackathon app with React + ASP.NET Core + PostgreSQL.

## Run Backend

```powershell
$env:DOTNET_CLI_HOME='c:\Users\DeepakDhoreSReddy\Desktop\Amiti-work\.dotnet'
dotnet run --project backend/FinanceTracker.Api/FinanceTracker.Api.csproj -c Release
```

Prerequisite:
- PostgreSQL must be running and reachable via `ConnectionStrings__FinanceDb` (or `appsettings.Development.json`).

Backend base URL:
- `http://localhost:5052`
- Health check: `http://localhost:5052/api/health`

## Run Frontend

```powershell
cd frontend
npm run dev
```

Frontend URL:
- `http://localhost:5173`

## Included in this starter
- Register/Login with JWT access token + refresh token rotation
- Accounts create/list
- Transactions create/list/update/delete (API)
- Budgets create/list with current-month utilization summary
- Dashboard summary API and UI widgets
- Category spend and recent transaction list

## Notes
- Persistence is PostgreSQL via EF Core.
- Startup applies EF migrations automatically (`Database.Migrate()`).
- A development safety guard now blocks non-local database hosts and blocked DB names
  (like `openlane`/`prod`) before the app starts.
- CORS defaults to localhost in development, and can be configured for deployment using
  `Cors__AllowedOrigins` settings.

## Azure Deployment Settings (App Service)
Use these environment variable names in Azure App Service:

- `ConnectionStrings__FinanceDb`
- `Jwt__SigningKey`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Cors__AllowedOrigins__0` = `https://<your-static-web-app-domain>`
- `ASPNETCORE_ENVIRONMENT` = `Production`

Frontend build variable (Static Web Apps):

- `VITE_API_BASE_URL` = `https://<your-api-app>.azurewebsites.net/api`

Production config template is available at:

- `backend/FinanceTracker.Api/appsettings.Production.json`

## Troubleshooting
- If you see recurring `FinanceTracker.Api.exe` application-error popups, use:
  `dotnet build backend/FinanceTracker.Api/FinanceTracker.Api.csproj -c Release`
  and run the DLL path:
  `dotnet backend/FinanceTracker.Api/bin/Release/net10.0/FinanceTracker.Api.dll`
- This avoids stale locked apphost EXE processes and gives clear console startup errors.
