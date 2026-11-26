# ATM

A simple ATM system built with .NET 10: an ASP.NET Core Minimal API backed by SQLite (EF Core), and a Blazor WebAssembly client. Users can view accounts, deposit, withdraw, transfer between accounts and review transaction history.

## Tech stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core Minimal APIs, OpenAPI + Swagger UI |
| Persistence | Entity Framework Core 10 + SQLite |
| Client | Blazor WebAssembly + Bootstrap |
| Tests | xUnit, Moq, bUnit, `Microsoft.AspNetCore.Mvc.Testing`, coverlet |

## Project structure

```
ATM.slnx
├── src/
│   ├── ATM.Domain/           Entities, money rules, domain exceptions
│   ├── ATM.Application/      Use cases (AtmService), DTOs, repository interfaces
│   ├── ATM.Infrastructure/   EF Core DbContext, migrations, repositories, DB logging
│   ├── ATM.Api/              Minimal API endpoints, error handling, CORS, Swagger
│   └── ATM.Client/           Blazor WebAssembly front end
└── tests/
    ├── ATM.Tests/            Unit + integration tests for Domain/Application/Infrastructure/API
    └── ATM.Client.Tests/     bUnit component and client service tests
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --list-sdks` should show a `10.0.x` entry)
- A trusted ASP.NET Core HTTPS development certificate
- Optional: Visual Studio 2022 17.14+ / Visual Studio 2026, or VS Code with the C# Dev Kit

No database server is needed — SQLite is file-based and created automatically.

## Setup

### Quick setup (Windows PowerShell)

[Scripts/Setup.ps1](Scripts/Setup.ps1) runs every setup step below. It checks for the .NET 10 SDK, trusts the dev certificate, then restores and builds the solution. It can also run the tests and start both apps:

```powershell
git clone https://github.com/rsaboriogap/ATM.git
cd ATM
.\Scripts\Setup.ps1                  # check SDK, trust cert, restore, build
.\Scripts\Setup.ps1 -RunTests -Run   # ...then run tests and start the API + client
```

| Switch | Effect |
|---|---|
| `-Configuration Release` | Build in Release (default `Debug`) |
| `-SkipCertificate` | Skip the HTTPS dev-certificate step |
| `-ResetDatabase` | Delete `atm.db` so it's recreated and reseeded |
| `-RunTests` | Run all tests after building |
| `-Coverage` | Run tests with code coverage |
| `-Run` | Start the API and client in two new windows |

If script execution is blocked, run `powershell -ExecutionPolicy Bypass -File .\Scripts\Setup.ps1` instead.

To set up manually, follow the steps below.

### 1. Clone the repository

```bash
git clone https://github.com/rsaboriogap/ATM.git
cd ATM
```

### 2. Trust the HTTPS development certificate

The client calls the API over HTTPS, so the dev certificate must be trusted:

```bash
dotnet dev-certs https --trust
```

### 3. Restore and build

```bash
dotnet restore ATM.slnx
dotnet build ATM.slnx
```

## Running the application

The API and the client run as two separate processes. Start both using their **`https`** launch profiles — the client is configured to call `https://localhost:7102/`, and the API only allows CORS requests from the client's `https` profile origins.

### Option A — command line

In one terminal, start the API:

```bash
dotnet run --project src/ATM.Api --launch-profile https
```

In a second terminal, start the client:

```bash
dotnet run --project src/ATM.Client --launch-profile https
```

### Option B — Visual Studio

1. Open `ATM.slnx`.
2. Right-click the solution → **Configure Startup Projects…** → **Multiple startup projects**.
3. Set both `ATM.Api` and `ATM.Client` to **Start**, and choose the `https` profile for each.
4. Press **F5**.

### URLs

| App | URL |
|---|---|
| Blazor client | https://localhost:7101 |
| API | https://localhost:7102 |
| Swagger UI (Development only) | https://localhost:7102/swagger |
| OpenAPI document | https://localhost:7102/openapi/v1.json |

### Database

On startup the API applies EF Core migrations automatically and creates `atm.db` in the API's working directory. It seeds two accounts if they don't exist:

| Account | Opening balance |
|---|---|
| Checking | $1,000.00 |
| Savings | $1,000.00 |

To reset the data, stop the API and delete `atm.db` (plus any `atm.db-shm` / `atm.db-wal` files). It will be recreated on the next run.

## Configuration

### API — `src/ATM.Api/appsettings*.json`

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:AtmDatabase` | `Data Source=atm.db;Foreign Keys=True;Default Timeout=30` | SQLite connection string |
| `Cors:AllowedOrigins` | `https://localhost:7101`, `http://localhost:5101` (Development) | Origins allowed to call the API |
| `Logging:Database:LogLevel` | `Information` | Minimum level for logs persisted to the `Logs` table |

### Client — `src/ATM.Client/wwwroot/appsettings*.json`

| Key | Default | Purpose |
|---|---|---|
| `Api:BaseUrl` | `https://localhost:7102/` | Base URL of the ATM API |
| `Api:TimeoutSeconds` | `30` | HTTP request timeout |

If you change the ports in either `launchSettings.json`, update `Api:BaseUrl` and `Cors:AllowedOrigins` to match.

## API overview

All endpoints are under `/api`. Amounts are in USD, must be between `0.01` and `1,000,000`, and may have at most two decimal places.

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/accounts` | List all accounts |
| `GET` | `/api/accounts/{id}` | Get an account by ID |
| `POST` | `/api/accounts/{id}/deposits` | Deposit funds — body: `{ "amount": 100.00 }` |
| `POST` | `/api/accounts/{id}/withdrawals` | Withdraw funds — body: `{ "amount": 50.00 }` |
| `GET` | `/api/accounts/{id}/transactions` | Get an account's transaction history |
| `POST` | `/api/transfers` | Transfer funds — body: `{ "sourceAccountId": "…", "destinationAccountId": "…", "amount": 25.00 }` |

Errors are returned as RFC 7807 `ProblemDetails` with an error code (e.g. `400` invalid amount, `404` account not found, `409` insufficient funds, `503` database busy).

You can try the endpoints from Swagger UI or with [src/ATM.Api/ATM.Api.http](src/ATM.Api/ATM.Api.http) in Visual Studio / VS Code.

## Running tests

Run all tests:

```bash
dotnet test ATM.slnx
```

Run a single test project:

```bash
dotnet test tests/ATM.Tests
dotnet test tests/ATM.Client.Tests
```

Integration tests use their own SQLite databases and don't touch `atm.db`, so the API doesn't need to be running.

### Code coverage

Collect coverage (Cobertura format) with coverlet:

```bash
dotnet test ATM.slnx --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

Results are written to `tests/*/TestResults/<guid>/coverage.cobertura.xml`. To generate an HTML report, install [ReportGenerator](https://github.com/danielpalme/ReportGenerator):

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"tests/**/TestResults/**/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:Html
```

## Database migrations

Migrations live in `src/ATM.Infrastructure/Persistence/Migrations`. To add one, install the EF Core CLI and run it with the API as the startup project:

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <MigrationName> \
  --project src/ATM.Infrastructure \
  --startup-project src/ATM.Api \
  --output-dir Persistence/Migrations
```

New migrations are applied automatically the next time the API starts.

## Troubleshooting

- **Client shows a network/CORS error** — make sure the API is running on `https://localhost:7102` and that both apps were started with the `https` profile. The client's `http` profile (port 5088) isn't in the API's allowed origins.
- **Browser warns about the certificate** — run `dotnet dev-certs https --trust` and restart the browser.
- **`503 Database temporarily unavailable`** — SQLite was locked by another writer. Check the operation's result (e.g. the transaction history) before retrying.
- **Port already in use** — stop the other process or change the port in `Properties/launchSettings.json`, then update the client/CORS settings as described under [Configuration](#configuration).
