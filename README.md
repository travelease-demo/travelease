# TravelEase – Travel Booking Management System

TravelEase is an internal travel booking management web application built with **ASP.NET Core MVC (.NET 8)** and **PostgreSQL**. It lets operations staff manage customers, trips and bookings, with live dashboards and reports.

It is a complete, working application, and it is also built as a **Docker learning project**. Configuration, logging, health checks, file storage and start-up behaviour are designed so you can practise every core Dockerfile instruction and container workflow against it (see [docs/DOCKER-LABS.md](docs/DOCKER-LABS.md)).

---

## 1. Features

| Module | What it does |
|---|---|
| **Dashboard** | Total revenue, bookings, customers and trips; confirmed/pending/cancelled split; monthly revenue chart; top destinations; recent bookings; next departures. |
| **Customers** | Add, edit, delete (blocked if the customer has bookings), view details, search by name/email/phone/city, filter by country, booking history per customer. |
| **Trips** | Add, edit, delete (blocked if booked); source/destination, transport type, operator, departure/arrival, seats, price, status; search and filter by status, transport and date range; occupancy meter. |
| **Bookings** | Create a booking (select customer + open trip + passengers) with a live price calculation; generates a reference such as `TE-261118-K7Q2M`; confirm, or cancel with a reason (seats are released); status tabs, search and date filters; booking timeline. |
| **Reports** | Revenue summary, monthly booking report (chart + table), booking status report, top-customer report, trip occupancy/revenue report, optional date range. **Export CSV** writes files to a configurable directory (a Docker volume). |
| **Settings** | Application name, version, environment, host/container ID, OS, runtime, process user, uptime, listening URLs, database status/latency/migrations, health check results, storage status, effective configuration (secrets masked). |
| **Health** | `/health`, `/health/live`, `/health/ready` with JSON output. |
| **REST API** | `/api/info`, `/api/dashboard/*`, `/api/customers`, `/api/trips`, `/api/bookings` (create, confirm, cancel). |

The UI uses Bootstrap 5 and includes a sidebar, a top bar, modal forms, client- and server-side validation, toast notifications, loading indicators and a responsive layout.

---

## 2. Architecture

```
 Browser ──HTTP──▶ ASP.NET Core (single process, Kestrel :8080)
                    │
                    ├─ MVC Controllers ──▶ Razor Views (Bootstrap 5, Chart.js)
                    ├─ API Controllers ──▶ JSON
                    ├─ Health checks  (/health, /health/live, /health/ready)
                    │
                    ├─ Services (business logic, DI-registered)
                    │    CustomerService · TripService · BookingService
                    │    DashboardService · ReportService · ReportFileService · SystemInfoService
                    │
                    ├─ EF Core DbContext ──Npgsql──▶ PostgreSQL  (Customers, Trips, Bookings)
                    ├─ DatabaseInitializer (background: migrate + seed, retry until DB is up)
                    └─ File storage: Storage:ReportsPath (CSV exports) + Data Protection keys
```

Design decisions:

* **One deployable application** (no microservices), using a layered structure: Controllers → Services → DbContext.
* **Configuration only through `appsettings*.json` and environment variables.** No credentials are in the code. The same build runs in Development, Staging and Production.
* **Background database initialisation.** The web server starts immediately. Migrations and seeding retry until PostgreSQL is reachable, so `/health/live` is 200 straight away and `/health/ready` becomes 200 once the database works. This is the behaviour you want when the app container starts before the database container.
* **Logs go to the console only.** Information and warnings go to stdout and errors go to stderr, in JSON in Production and single-line text in Development. You read them with `docker logs`.
* **Seat inventory is consistent.** A booking decrements seats with an atomic conditional `UPDATE` inside a transaction, so concurrent requests cannot oversell a trip. Cancelling a booking releases its seats.
* **Built-in health probe.** `dotnet TravelEase.dll healthcheck` lets `HEALTHCHECK` work in images without curl.

---

## 3. Technology stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8 LTS, ASP.NET Core MVC, Kestrel |
| Language | C# 12 |
| UI | Razor Views, Bootstrap 5.3, Bootstrap Icons, Chart.js 4, jQuery Validation (unobtrusive), vanilla JS |
| Data | Entity Framework Core 8, Npgsql provider, PostgreSQL 16 |
| Ops | ASP.NET Core Health Checks, Microsoft.Extensions.Logging (console/JSON), Data Protection |
| Containers | Multi-stage Dockerfiles, Docker Compose |

Front-end libraries are loaded from public CDNs (cdnjs, Google Fonts), so the **browser** needs internet access. The server needs no external services or API keys.

---

## 4. Prerequisites

Choose one way to run the app:

| Option | You need |
|---|---|
| **A. Docker only** (recommended) | Docker Desktop / Docker Engine with Compose v2 |
| **B. Local .NET** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and a PostgreSQL 13+ server (a Docker container is fine) |

Optional: the `dotnet-ef` tool for working with migrations by hand: `dotnet tool install --global dotnet-ef --version 8.0.11`.

---

## 5. Quick start with Docker Compose

```bash
cd TravelEase
cp .env.example .env            # Windows PowerShell: Copy-Item .env.example .env
# edit .env and set DB_PASSWORD

docker compose up -d --build
docker compose ps               # wait until app and db show "healthy"
```

Open **http://localhost:8080**. The database schema is created and sample data is inserted automatically.

```bash
docker compose logs -f app      # follow the application logs
docker compose down             # stop (data is kept in volumes)
docker compose down -v          # stop and DELETE the database + report volumes
```

---

## 6. Running locally with the .NET SDK

### 6.1 Install dependencies

```bash
cd TravelEase
dotnet restore
```

### 6.2 Configure PostgreSQL

The easiest option is to run PostgreSQL in a container:

```bash
docker run -d --name travelease-db \
  -e POSTGRES_DB=travelease -e POSTGRES_USER=travelease -e POSTGRES_PASSWORD=devpassword \
  -p 5432:5432 -v travelease-devdb:/var/lib/postgresql/data postgres:16-alpine
```

If you use an existing server, create the role and database:

```sql
CREATE ROLE travelease WITH LOGIN PASSWORD 'devpassword' CREATEDB;
CREATE DATABASE travelease OWNER travelease;
```

`appsettings.Development.json` points to `Host=localhost;Port=5432;Database=travelease;Username=travelease` with **no password**. Supply the password through an environment variable:

```bash
# bash / zsh
export DB_PASSWORD=devpassword
# PowerShell
$env:DB_PASSWORD = "devpassword"
```

### 6.3 Run the application

```bash
dotnet run --launch-profile "TravelEase (Development)"
```

The app listens on **http://localhost:5080**. Use the `TravelEase (Staging)` or `TravelEase (Production)` profiles to try the other environments.

### 6.4 Database migrations

Migrations are in `Migrations/` and are **applied automatically at start-up** (`Database:ApplyMigrationsOnStartup=true`). To manage them by hand:

```bash
dotnet ef database update                       # apply migrations
dotnet ef migrations add <Name>                 # after changing the model
dotnet ef migrations script -o schema.sql       # SQL script for a DBA / CI pipeline
```

The design-time factory (`Data/TravelEaseDbContextFactory.cs`) reads the same configuration, so `DB_HOST`, `DB_PASSWORD` and the other variables work with `dotnet ef` too. To let a pipeline own the schema, set `Database__ApplyMigrationsOnStartup=false`.

### 6.5 Sample data seeding

On first start against an **empty** database, `SampleDataSeeder` inserts:

* 40 customers (India, UAE, UK, Europe, APAC, North America)
* 36 trips (flights, trains, buses, cruises) from about 10 months ago to 4 months ahead
* About 220 bookings in confirmed, pending and cancelled states, with consistent seat counts

Seeding is idempotent: it is skipped when customers already exist. To disable it, set `Database__SeedSampleData=false`. To re-seed, drop the database (`docker compose down -v`) and start again.

---

## 7. Application URLs

| URL | Page |
|---|---|
| `/` | Dashboard |
| `/customers`, `/customers/details/{id}` | Customers |
| `/trips`, `/trips/details/{id}` | Trips |
| `/bookings`, `/bookings/details/{id}` | Bookings |
| `/reports` | Reports and generated files |
| `/settings` | Application, database and health information |
| `/api/info` | Which host, version and environment served the request (JSON) |
| `/api/dashboard/summary`, `/api/dashboard/monthly-revenue` | Dashboard data |
| `/api/customers`, `/api/trips`, `/api/trips/bookable`, `/api/bookings` | List endpoints (`?search=&page=&pageSize=`) |
| `POST /api/bookings` | `{"customerId":1,"tripId":30,"passengers":2,"confirmImmediately":true}` |
| `POST /api/bookings/{id}/confirm`, `POST /api/bookings/{id}/cancel` | `{"reason":"..."}` |

## 8. Health check URLs

| Endpoint | Checks | Status codes | Use for |
|---|---|---|---|
| `/health` | All checks | 200 Healthy/Degraded, 503 Unhealthy | Humans and dashboards |
| `/health/live` | Process is running (`self`) | 200 / 503 | Kubernetes **livenessProbe** |
| `/health/ready` | PostgreSQL connection, migrations and seeding complete, reports directory writable | 200 Healthy/Degraded, 503 Unhealthy | Docker **HEALTHCHECK**, Kubernetes **readinessProbe** |

Example response:

```json
{
  "status": "Healthy",
  "application": "TravelEase",
  "version": "1.0.0",
  "environment": "Production",
  "host": "c234d6bddeff",
  "totalDurationMs": 4.1,
  "checks": [
    { "name": "database", "status": "Healthy", "description": "PostgreSQL is reachable", "data": { "latencyMs": 2.3 } },
    { "name": "database-initialisation", "status": "Healthy" },
    { "name": "report-storage", "status": "Healthy" }
  ]
}
```

A storage problem returns **Degraded**, which still gives HTTP 200: the app can serve traffic, but exports fail.

---

## 9. Configuration and environment variables

ASP.NET Core reads settings in this order, and later sources win: `appsettings.json` → `appsettings.{Environment}.json` → **environment variables** → command-line arguments. In environment variables, nested keys use a double underscore: `Database:SeedSampleData` becomes `Database__SeedSampleData`.

### Environment and port

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development`, `Staging` or `Production`. Selects the `appsettings.{env}.json` file, the error pages and the log format. |
| `ASPNETCORE_HTTP_PORTS` | `8080` (in the images) | Internal HTTP port(s), for example `9090` or `8080;8081`. |
| `ASPNETCORE_URLS` | – | Alternative full binding, for example `http://+:9090`. Overrides `ASPNETCORE_HTTP_PORTS`. |

### Database

| Variable | Default | Purpose |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | *(empty)* | Full Npgsql connection string. If set, it is used as the base. |
| `DB_HOST` / `DB_PORT` / `DB_NAME` / `DB_USER` | `localhost` / `5432` / `travelease` / `travelease` | Individual parts. They override the matching parts of the connection string. |
| `DB_PASSWORD` | – | Password, used when the connection string has none. |
| `DB_PASSWORD_FILE` | – | Path to a file that contains the password (Docker or Kubernetes secrets). Takes priority over `DB_PASSWORD`. |
| `Database__ApplyMigrationsOnStartup` | `true` | Run EF Core migrations at start-up. |
| `Database__SeedSampleData` | `true` | Insert sample data into an empty database. |
| `Database__MaxStartupRetries` | `0` (forever) | Database connection attempts before giving up. Development uses `3`. |
| `Database__RetryDelaySeconds` | `5` | Base delay between attempts. Back-off is capped at 60 s. |
| `Database__CommandTimeoutSeconds` | `30` | SQL command timeout. |

### Application and storage

| Variable | Default | Purpose |
|---|---|---|
| `App__Name` | `TravelEase` | Name shown in the UI and logs. |
| `App__Version` | *(assembly version)* | Overrides the displayed version. The images set it from the `APP_VERSION` build argument. |
| `App__CurrencySymbol` | `₹` | Currency symbol in the UI. |
| `App__Culture` | `en-IN` | Number grouping culture. Falls back to invariant if ICU is missing. |
| `Storage__ReportsPath` | `data/reports` (`/app/data/reports` in the images) | Where CSV exports are written. **Mount a volume here.** |
| `Storage__DataProtectionKeysPath` | `data/keys` (`/app/data/keys` in the images) | Antiforgery key ring. Persist it so forms keep working after a restart. |
| `HEALTHCHECK_PORT` | – | Port used by the built-in `healthcheck` probe, if it differs from `ASPNETCORE_HTTP_PORTS`. |

### Logging

| Variable | Example | Purpose |
|---|---|---|
| `Logging__LogLevel__Default` | `Debug` | Global minimum level. |
| `Logging__LogLevel__TravelEase` | `Debug` | Level for application categories. |
| `Logging__Console__FormatterName` | `json` / `simple` / `systemd` | Log format. Production uses `json` and Development uses `simple`. |

Events that are logged: start-up (version, environment, host, user, PID, masked DB target), listening URLs, shutdown (stopping and stopped), migrations and seeding, **database connection failures and retries**, failed DB health checks, **booking created / confirmed / cancelled**, customer and trip changes, report exports, unhandled exceptions with request ID, and 404s.

---

## 10. Project structure

```
TravelEase/
├── Configuration/          Options classes, PostgreSQL connection-string resolver
├── Controllers/            MVC controllers (Dashboard, Customers, Trips, Bookings, Reports, Settings, Error)
│   └── Api/                REST API controllers + DTOs
├── Data/                   DbContext, design-time factory, sample data seeder
├── HealthChecks/           Database, initialisation and storage checks + JSON response writer
├── Infrastructure/         Background DB initialiser, health probe CLI, UI formatter, helpers
├── Migrations/             EF Core migrations (InitialCreate)
├── Models/                 Entities (Customer, Trip, Booking), enums, ServiceResult
│   └── ViewModels/         Form models (validation) and page view models
├── Services/               Business logic (customers, trips, bookings, dashboard, reports, files, system info)
├── Views/
│   ├── Dashboard/  Customers/  Trips/  Bookings/  Reports/  Settings/
│   └── Shared/             _Layout, pagination, status badge, error page
├── wwwroot/
│   ├── css/site.css        Design system and dashboard styles
│   ├── js/site.js          Modals, AJAX forms, validation, toasts, price calculator
│   ├── js/charts.js        Chart.js charts
│   └── images/             Logo and favicon
├── Properties/launchSettings.json   Development / Staging / Production profiles
├── docker/
│   ├── Dockerfile.instructions      All 15 Dockerfile instructions, annotated
│   ├── Dockerfile.single-stage      Naive image, for size and behaviour comparison
│   ├── Dockerfile.published         Image built from pre-published files
│   ├── Dockerfile.alpine            Size-optimised Alpine runtime
│   ├── docker-entrypoint.sh         ENTRYPOINT script (exec "$@", optional wait-for-db)
│   └── assets/sample-reports.tar.gz Archive extracted by ADD
├── docs/
│   ├── BASICS-FAQ.md            Beginner Q&A: OS of images, build stages, config, DB flow, data storage
│   ├── DOCKERFILE-EXPLAINED.md  The Dockerfile explained line by line (teaching guide)
│   └── DOCKER-LABS.md           Step-by-step Docker exercises
├── Dockerfile              Production multi-stage Dockerfile (recommended)
├── docker-compose.yml      App + PostgreSQL with health-gated start-up and volumes
├── .dockerignore  .env.example  .gitattributes  .gitignore
├── appsettings.json  appsettings.Development.json  appsettings.Staging.json  appsettings.Production.json
├── Program.cs
└── TravelEase.csproj
```

---

## 11. Docker summary

| Goal | Command |
|---|---|
| Build the production image | `docker build -t travelease:1.0.0 --build-arg APP_VERSION=1.0.0 .` |
| Run it against PostgreSQL on the host | `docker run -d --name travelease -p 8080:8080 -e DB_HOST=host.docker.internal -e DB_PASSWORD=devpassword -v travelease-data:/app/data travelease:1.0.0` |
| Change the port | `-e ASPNETCORE_HTTP_PORTS=9090 -p 9090:9090` |
| Container health | `docker inspect --format '{{.State.Health.Status}}' travelease` |
| Logs | `docker logs -f travelease` |
| Export published files without the SDK | `docker build --target export --output publish .` |
| All instructions demo | `docker build -f docker/Dockerfile.instructions -t travelease:instructions .` |

**[docs/DOCKER-LABS.md](docs/DOCKER-LABS.md)** has the full set of guided exercises: every instruction, image size comparison, CMD vs ENTRYPOINT, volumes, non-root users, health checks, build arguments, and Compose.
New to all of this? Start with **[docs/BASICS-FAQ.md](docs/BASICS-FAQ.md)**, then read **[docs/DOCKERFILE-EXPLAINED.md](docs/DOCKERFILE-EXPLAINED.md)**.

---

## 12. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `/health/ready` returns 503 and the logs show `Database connection failed ... Retrying` | PostgreSQL is not reachable. Check `DB_HOST`. Inside a container, `localhost` is the container itself: use the Compose service name (`db`) or `host.docker.internal`. |
| `password authentication failed` | `DB_PASSWORD` does not match. With Compose, the password is only applied when the **pgdata volume is first created**. Run `docker compose down -v` to reset it. |
| Reports page shows **Not writable** | The volume is owned by another UID. The images run as UID 1654 (`Dockerfile`) or 10001 (`Dockerfile.instructions`). Fix ownership or use a named volume. |
| Forms fail with HTTP 400 after a container restart | The antiforgery keys were not persisted. Mount a volume at `/app/data`. |
| `exec docker-entrypoint.sh: no such file or directory` | The script has Windows CRLF line endings. `.gitattributes` forces LF, and `Dockerfile.instructions` also strips CR. |
| Page has no styles | The browser cannot reach the CDNs (cdnjs, Google Fonts). |
