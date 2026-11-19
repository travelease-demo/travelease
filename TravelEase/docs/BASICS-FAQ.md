# TravelEase – Basics FAQ (Docker, .NET and the Database)

A question-and-answer guide for **freshers / college graduates** who are seeing Docker, ASP.NET Core
and PostgreSQL together for the first time. Every answer refers to the **real files in this project**,
and the facts (OS versions, sizes, paths, user IDs) were checked against the actual images.

> Companion documents:
> [DOCKERFILE-EXPLAINED.md](DOCKERFILE-EXPLAINED.md) – the Dockerfile line by line ·
> [DOCKER-LABS.md](DOCKER-LABS.md) – hands-on exercises ·
> [../README.md](../README.md) – how to run the project

---

## Contents

1. [The big picture in one diagram](#1-the-big-picture-in-one-diagram)
2. [Docker basics](#2-docker-basics)
3. [Base images and operating systems](#3-base-images-and-operating-systems)
4. [How .NET builds the app (restore → build → publish)](#4-how-net-builds-the-app-restore--build--publish)
5. [appsettings.json and configuration](#5-appsettingsjson-and-configuration)
6. [How the app connects to the database – from where to where](#6-how-the-app-connects-to-the-database--from-where-to-where)
7. [How the database container gets created](#7-how-the-database-container-gets-created)
8. [Where is the data actually stored?](#8-where-is-the-data-actually-stored)
9. [Networking and ports](#9-networking-and-ports)
10. [Running containers: users, logs, health, shutdown](#10-running-containers-users-logs-health-shutdown)
11. [Important files every DevOps engineer should know](#11-important-files-every-devops-engineer-should-know)
12. [Quick-fire questions (interview style)](#12-quick-fire-questions-interview-style)
13. [Troubleshooting: symptom → cause → fix](#13-troubleshooting-symptom--cause--fix)
14. [More questions learners usually ask](#14-more-questions-learners-usually-ask)
    - [14.1 Getting started](#141-getting-started)
    - [14.2 Docker commands and flags](#142-docker-commands-and-flags)
    - [14.3 Images, tags and disk space](#143-images-tags-and-disk-space)
    - [14.4 Docker Compose](#144-docker-compose)
    - [14.5 Database and data](#145-database-and-data)
    - [14.6 The ASP.NET Core application itself](#146-the-aspnet-core-application-itself)
    - [14.7 Security](#147-security)
    - [14.8 Going to production (CI/CD, cloud, Kubernetes)](#148-going-to-production-cicd-cloud-kubernetes)
15. [Glossary](#15-glossary)

---

## 1. The big picture in one diagram

```
 YOUR LAPTOP (Windows)
 ┌──────────────────────────────────────────────────────────────────────────────────────┐
 │  Browser ── http://localhost:8080 ──┐                                                │
 │                                     │  port mapping 8080 (host) -> 8080 (container)  │
 │  Docker Desktop  (runs a small Linux VM using WSL2)                                  │
 │  ┌──────────────────────────────────┼───────────────────────────────────────────┐   │
 │  │  network: travelease_default     ▼                                           │   │
 │  │  ┌──────────────────────────────────────┐      ┌────────────────────────────┐ │   │
 │  │  │ container "app"                      │      │ container "db"             │ │   │
 │  │  │ image : travelease:1.0.0             │ TCP  │ image : postgres:16-alpine │ │   │
 │  │  │ OS    : Debian 12 (bookworm)         │─────▶│ OS    : Alpine Linux 3.24  │ │   │
 │  │  │ runs  : dotnet TravelEase.dll        │ 5432 │ runs  : postgres 16        │ │   │
 │  │  │ user  : app (uid 1654)               │ "db" │ data  : /var/lib/postgresql│ │   │
 │  │  │ data  : /app/data                    │      │         /data              │ │   │
 │  │  └───────────────┬──────────────────────┘      └─────────────┬──────────────┘ │   │
 │  │                  │                                           │                │   │
 │  │          volume "appdata"                            volume "pgdata"          │   │
 │  │          (reports, keys)                             (all database rows)      │   │
 │  └──────────────────────────────────────────────────────────────────────────────┘   │
 └──────────────────────────────────────────────────────────────────────────────────────┘
```

**In one sentence:** the browser talks to the **app container** through port 8080; the app talks
to the **db container** by the name `db` on port 5432; the database writes its files into a
**volume**, so the data survives even if the containers are deleted.

---

## 2. Docker basics

### Q: What problem does Docker solve?
"It works on my machine" – the app needs a specific OS, a specific .NET runtime, specific libraries,
specific settings. Docker packs **the app + everything it needs to run** into one **image**. Any
machine with Docker can run that image and get exactly the same result.

### Q: Image vs container – what is the difference?

| | Image | Container |
|---|---|---|
| What it is | A read-only **template** (files + settings) | A **running instance** of an image |
| Analogy | A class / an `.iso` file / a recipe | An object / a running VM / the cooked dish |
| Created by | `docker build` | `docker run` / `docker compose up` |
| Changes? | Never – it is immutable | Has a thin writable layer; lost when the container is removed |
| Example here | `travelease:1.0.0`, `postgres:16-alpine` | `travelease-app-1`, `travelease-db-1` |

One image can start many containers.

### Q: Is a container a virtual machine?
**No.** A VM has its own full operating system **including its own kernel**. A container is just a
normal Linux process that is **isolated** (it gets its own file system view, network, process list
and user IDs) but it **shares the host's kernel**. That is why a container starts in under a second
and an image can be a few hundred MB instead of many GB.

### Q: What are layers?
Each instruction in a Dockerfile that changes files (`FROM`, `RUN`, `COPY`, `ADD`) creates a
**layer** – a diff of the file system. Layers are stacked to form the image and are **cached and
shared**. If two images use `mcr.microsoft.com/dotnet/aspnet:8.0`, those base layers are stored once.
See them with:

```powershell
docker image history travelease:1.0.0
```

Real output (top of it) – note `COPY /app/publish .` is only **8 MB**; the rest is the base image:

```
0B      ENTRYPOINT ["dotnet" "TravelEase.dll"]
0B      HEALTHCHECK ...
0B      EXPOSE [8080/tcp]
0B      VOLUME [/app/data]
0B      USER 1654
8.01MB  COPY /app/publish .
...     (base image layers: Debian + .NET runtime + ASP.NET Core runtime)
```

Instructions like `ENV`, `EXPOSE`, `USER`, `LABEL` show `0B` – they change **metadata**, not files.

### Q: What is a registry?
A server that stores images, like GitHub for images. `docker pull` downloads, `docker push` uploads.

| Image name | Registry |
|---|---|
| `mcr.microsoft.com/dotnet/aspnet:8.0` | Microsoft Container Registry (MCR) |
| `postgres:16-alpine` | Docker Hub (the default when no host name is written) |
| `travelease:1.0.0` | Only on your machine (we built it, never pushed) |

### Q: What is Docker Compose?
A tool that reads [docker-compose.yml](../docker-compose.yml) and starts **several containers
together** with their networks and volumes, in the right order. Instead of typing two long
`docker run` commands, you type `docker compose up -d`.

---

## 3. Base images and operating systems

### Q: If we use a base image, which operating system is the container using?
The **user-space** of the OS comes from the base image (the files: `/bin`, `/etc`, libraries,
package manager, shell). The **kernel** always comes from the host. Checked on this project:

| Image | Used for | OS inside (`/etc/os-release`) | Size |
|---|---|---|---|
| `mcr.microsoft.com/dotnet/sdk:8.0` | **building** the app (build stage) | Debian GNU/Linux 12 (bookworm) | ~1.24 GB |
| `mcr.microsoft.com/dotnet/aspnet:8.0` | **running** the app (final stage) | Debian GNU/Linux 12 (bookworm) | ~ 322 MB |
| `travelease:1.0.0` (our image) | the TravelEase app | Debian 12 (inherited from aspnet:8.0) | 333 MB (= 322 MB base + our ~8 MB app + metadata) |
| `postgres:16-alpine` | the database | Alpine Linux 3.24 | ~ 420 MB |
| `docker/Dockerfile.alpine` result | alternative app image | Alpine Linux | 224 MB |

Check it yourself:

```powershell
docker run --rm --entrypoint cat travelease:1.0.0 /etc/os-release
docker run --rm postgres:16-alpine cat /etc/os-release
```

So **our app container is Debian 12** and **the database container is Alpine Linux** – two different
distributions running side by side on the same kernel. That is completely normal.

### Q: I am on Windows. How does a Linux container run?
Docker Desktop starts a small **Linux virtual machine** in the background (through **WSL2**). All
Linux containers run inside that VM and share **its** kernel. You can see it:

```powershell
docker run --rm --entrypoint uname travelease:1.0.0 -r
# 7.0.14-linuxkit      <- the kernel of Docker Desktop's VM, not Debian's and not Windows'
```

### Q: What is the "tag" (`:8.0`, `:16-alpine`)?
A label for a version/variant of an image. Common .NET tags:

| Tag | Meaning |
|---|---|
| `8.0` | latest .NET 8 patch, Debian based (what we use) |
| `8.0-alpine` | .NET 8 on Alpine – smaller, uses musl instead of glibc |
| `8.0-bookworm-slim` | explicit Debian 12 slim variant |
| `8.0-jammy-chiseled` / `8.0-noble-chiseled` | Ubuntu "chiseled": no shell, no package manager, very small, very secure |

> **Production tip:** `:8.0` moves when Microsoft publishes a patch. For fully repeatable builds pin
> an exact version (`8.0.11`) or a digest (`@sha256:...`).

### Q: Why is the build stage on `sdk` and the final stage on `aspnet`?

| Image | Contains | Needed for |
|---|---|---|
| `sdk` | compiler (Roslyn), MSBuild, NuGet, dotnet CLI, **plus** the runtimes | building |
| `aspnet` | .NET runtime + ASP.NET Core runtime only | running a web app |
| `runtime` | .NET runtime only (no ASP.NET Core) | running console apps / workers |
| `runtime-deps` | only Linux libraries .NET needs, no .NET at all | self-contained apps |

You **compile** with the SDK and **ship** only the runtime. That is the whole idea of multi-stage
builds: 1.58 GB (single-stage, SDK shipped) vs 333 MB (multi-stage).

### Q: Does the container have `bash`, `apt`, `curl`?
`aspnet:8.0` (Debian) has `sh`, `bash` and `apt-get`, but **no `curl` or `wget`**. That's why our
HEALTHCHECK uses the app itself (`dotnet TravelEase.dll healthcheck`). Alpine images have `sh` (not
`bash`) and use `apk` instead of `apt-get`. Chiseled images have **no shell at all**.

---

## 4. How .NET builds the app (restore → build → publish)

### Q: What are the stages of an MSBuild / dotnet build?

```
 TravelEase.csproj
       │
       ▼
 ┌─────────────┐   downloads NuGet packages listed in <PackageReference>
 │ 1. RESTORE  │   -> ~/.nuget/packages/ (package cache)
 └──────┬──────┘   -> obj/project.assets.json (the resolved dependency graph)
        ▼
 ┌─────────────┐   compiles C# (*.cs) with Roslyn  -> IL code
 │ 2. BUILD    │   compiles Razor views (*.cshtml) into the same DLL
 └──────┬──────┘   -> obj/Release/net8.0/ (intermediate) and bin/Release/net8.0/ (output)
        ▼
 ┌─────────────┐   collects everything needed to RUN the app into one folder:
 │ 3. PUBLISH  │   TravelEase.dll + dependency DLLs + wwwroot + appsettings*.json
 └─────────────┘   + TravelEase.deps.json + TravelEase.runtimeconfig.json + web.config
                   -> the --output folder (/app/publish in our Dockerfile)
```

### Q: Does `dotnet publish` also run restore? And build?
**Yes, both.** Each command automatically runs the steps before it:

| Command | Runs restore? | Runs build? | Then |
|---|---|---|---|
| `dotnet restore` | ✅ | ❌ | – |
| `dotnet build` | ✅ (implicit) | ✅ | – |
| `dotnet publish` | ✅ (implicit) | ✅ (implicit) | copies to publish folder |
| `dotnet run` | ✅ | ✅ | starts the app (development only) |
| `dotnet publish --no-restore` | ❌ | ✅ | copies to publish folder |
| `dotnet publish --no-build` | ❌ | ❌ | only copies (needs a previous build) |

### Q: Then why does our Dockerfile run `dotnet restore` separately and then `publish --no-restore`?
For **Docker layer caching**:

```dockerfile
COPY TravelEase.csproj ./              # layer A: only the project file
RUN dotnet restore TravelEase.csproj   # layer B: packages (slow, ~30-60 s)
COPY . .                               # layer C: all source code
RUN dotnet publish ... --no-restore    # layer D: compile + publish
```

When you change a `.cs` or `.cshtml` file, only layer C changes, so Docker **reuses cached layer B**
and does not download packages again. Packages are only downloaded again when the `.csproj` changes.
`--no-restore` simply says "restore already happened in the previous layer, don't repeat it".

### Q: What is in the publish output? (what actually goes into the image)
Real listing of `/app` in our image:

| File | What it is |
|---|---|
| `TravelEase.dll` | **Our app**: all C# code **and all Razor views compiled** (there is no `Views/` folder in the image!) |
| `TravelEase.pdb` | Debug symbols – gives line numbers in stack traces |
| `TravelEase.deps.json` | List of dependencies and where to find them |
| `TravelEase.runtimeconfig.json` | Which runtime to use: `Microsoft.NETCore.App 8.0` + `Microsoft.AspNetCore.App 8.0` |
| `Npgsql.dll`, `Npgsql.EntityFrameworkCore.PostgreSQL.dll` | PostgreSQL driver + EF Core provider |
| `Microsoft.EntityFrameworkCore*.dll` | EF Core (ORM) |
| `appsettings*.json` | Configuration files (section 5) |
| `wwwroot/` | Static files: CSS, JS, images |
| `web.config` | Only used by IIS on Windows – ignored on Linux |
| `data/` | Created by our Dockerfile (`RUN mkdir`), owned by user `app`, mounted as a volume |

Notice: **no `.cs` source code** is shipped. Only compiled output.

### Q: What is IL / why `dotnet TravelEase.dll` and not `./TravelEase`?
C# is compiled to **IL (Intermediate Language)**, not machine code. At run time the .NET runtime
**JIT-compiles** IL into machine code for the CPU it's running on. `dotnet TravelEase.dll` means
"start the .NET runtime and run this IL". We pass `-p:UseAppHost=false` so publish does **not** also
create a native launcher file `TravelEase` – we don't need it because we always start via `dotnet`.

### Q: Release vs Debug?
`--configuration Release` turns on compiler optimisations and disables debug-only code. Always use
Release for images. Debug is for your IDE.

### Q: Where are the NuGet packages cached?
On a developer machine: `%USERPROFILE%\.nuget\packages`. Inside the build stage: `/root/.nuget/packages`.
That folder is in the **build stage only** – it is **not** copied to the final image.

### Q: What is a self-contained publish?
`dotnet publish --self-contained -r linux-x64` bundles the .NET runtime itself into the output, so the
target machine needs no .NET installed. Then you would use the tiny `runtime-deps` base image. We use
the default **framework-dependent** publish (smaller output, runtime comes from the base image).

---

## 5. appsettings.json and configuration

### Q: What is `appsettings.json` used for?
It stores the **settings** of the app – things that should be changeable **without recompiling**:
database settings, log levels, file paths, currency symbol, feature switches. Our
[appsettings.json](../appsettings.json):

```json
{
  "App":       { "Name": "TravelEase", "CurrencySymbol": "₹", ... },
  "ConnectionStrings": { "DefaultConnection": "" },          <- intentionally EMPTY
  "Database":  { "ApplyMigrationsOnStartup": true, "SeedSampleData": true, ... },
  "Storage":   { "ReportsPath": "data/reports", "DataProtectionKeysPath": "data/keys" },
  "Logging":   { "LogLevel": { ... }, "Console": { "FormatterName": "json", ... } }
}
```

The code reads it, for example `builder.Configuration.GetSection("Database")` in
[Program.cs](../Program.cs).

### Q: Why are there four appsettings files?

| File | Loaded when | Contains here |
|---|---|---|
| `appsettings.json` | **always** | the defaults for everything |
| `appsettings.Development.json` | `ASPNETCORE_ENVIRONMENT=Development` | `localhost` DB, readable single-line logs, Debug log level |
| `appsettings.Staging.json` | `ASPNETCORE_ENVIRONMENT=Staging` | sample data on, Debug logs for our code |
| `appsettings.Production.json` | `ASPNETCORE_ENVIRONMENT=Production` | Information-level logs |

The environment-specific file is loaded **on top of** `appsettings.json` and only overrides the keys
it contains. Our Dockerfile sets `ASPNETCORE_ENVIRONMENT=Production` as the default.

### Q: In what order is configuration loaded? (who wins?)
Later sources **override** earlier ones:

```
1. appsettings.json                        (lowest priority)
2. appsettings.{Environment}.json
3. User secrets                            (Development only; not used in this project)
4. Environment variables                   <- what Docker uses: ENV, -e, compose "environment:"
5. Command-line arguments                  (highest priority)
```

That's why **the same image** can run in Dev, Staging and Production: the image is identical, only
the environment variables differ.

### Q: How does an environment variable map to a JSON key?
Use **double underscore `__`** in place of the `:` (nesting):

| JSON path | Environment variable |
|---|---|
| `Database:SeedSampleData` | `Database__SeedSampleData=false` |
| `App:CurrencySymbol` | `App__CurrencySymbol=$` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection=Host=...` |
| `Logging:LogLevel:Default` | `Logging__LogLevel__Default=Warning` |

Example: `docker run -e App__CurrencySymbol='$' ...` changes the currency without touching any file.

### Q: Are appsettings files inside the image? Can I edit them in a running container?
Yes, they are copied by `publish` into `/app`. But **don't edit them inside a container** – the change
is lost when the container is recreated, and the files are owned by root (read-only for user `app`).
Override with **environment variables** instead, or mount a file with `-v`.

### Q: Why is the password not in appsettings.json?
Because appsettings.json is committed to Git and copied into the image. Anyone with the image or the
repository would see the password. The password comes **only** from `DB_PASSWORD` (environment
variable) or `DB_PASSWORD_FILE` (a mounted secret file). Locally it lives in `.env`, which is listed in
both `.gitignore` and `.dockerignore`.

### Q: What is `launchSettings.json`?
[Properties/launchSettings.json](../Properties/launchSettings.json) is used **only** by `dotnet run`
/ Visual Studio on a developer machine (port 5080, Development environment). It is **not** used in
Docker and is not even published.

---

## 6. How the app connects to the database – from where to where

### Q: Walk me through one request end to end
User opens **Customers** page:

```
 1. Browser          GET http://localhost:8080/customers
 2. Docker Desktop   host port 8080  ──forwards──▶  app container port 8080
 3. Kestrel          (ASP.NET Core web server inside the app) receives the request
 4. Routing          /customers  ->  CustomersController.Index()
 5. Controller       calls ICustomerService (injected by dependency injection)
 6. Service          runs a LINQ query on TravelEaseDbContext
 7. EF Core          translates LINQ to SQL: SELECT ... FROM "Customers" ...
 8. Npgsql           (PostgreSQL driver) opens a TCP connection to  db:5432
 9. Docker DNS       resolves the name "db" -> IP of the db container (e.g. 172.18.0.2)
10. PostgreSQL       runs the SQL, reads rows from files in /var/lib/postgresql/data
                     (which is the "pgdata" volume)
11. Back up          rows -> Npgsql -> EF Core objects -> Service -> Controller
12. Razor view       Customers/Index.cshtml renders HTML
13. Browser          shows the page
```

### Q: Where does the app get the connection details from?
[Configuration/DatabaseConnectionResolver.cs](../Configuration/DatabaseConnectionResolver.cs) builds
the connection string at start-up:

```
 Option 1:  ConnectionStrings__DefaultConnection = "Host=db;Port=5432;Database=travelease;Username=travelease;Password=..."
 Option 2:  DB_HOST=db  DB_PORT=5432  DB_NAME=travelease  DB_USER=travelease   (+ password below)

 Password:  DB_PASSWORD=...            or   DB_PASSWORD_FILE=/run/secrets/db_password
```

In Compose we use option 2 (see the `app:` → `environment:` section of
[docker-compose.yml](../docker-compose.yml)). The values come from your `.env` file. The app logs the
final target **with the password masked**:

```
Database target: Host=db;Port=5432;Database=travelease;Username=travelease;Password=********;...
```

### Q: Why `DB_HOST=db` and not `localhost`?
**Very common confusion.** Inside a container, `localhost` means **that same container**. The app
container has no PostgreSQL inside it, so `localhost:5432` would fail ("connection refused"). On the
Compose network, every service is reachable by its **service name** – our database service is named
`db`, so the host name is `db`.

| Where is the app running? | Where is PostgreSQL? | Use DB_HOST = |
|---|---|---|
| Container (Compose) | Container in the same Compose file | `db` (service name) |
| Container (`docker run`) | On your Windows machine | `host.docker.internal` |
| On Windows (`dotnet run`) | Container with `ports: "5432:5432"` | `localhost` |
| Container | Cloud DB (Azure/AWS RDS) | the cloud host name |

### Q: What creates the tables? Did someone run SQL scripts?
No manual SQL. **EF Core migrations** do it:

1. A developer ran `dotnet ef migrations add InitialCreate` once. That generated C# code in
   [Migrations/](../Migrations/) describing the tables (`Customers`, `Trips`, `Bookings`, indexes,
   constraints).
2. That code is **compiled into TravelEase.dll**.
3. On start-up, [Infrastructure/DatabaseInitializer.cs](../Infrastructure/DatabaseInitializer.cs)
   calls `db.Database.MigrateAsync()`, which:
   - creates the table `__EFMigrationsHistory` if missing,
   - checks which migrations are already applied,
   - runs only the missing ones.
4. Then `SampleDataSeeder` inserts demo data **only if the Customers table is empty**
   (40 customers, 36 trips, ~220 bookings). On the next start it logs
   `Sample data seeding skipped: database already contains data`.

### Q: What if the database is not ready when the app starts?
`DatabaseInitializer` runs in the background and **retries** (5 s, 10 s, ... up to 60 s between tries).
Meanwhile the website is already up: `/health/live` returns 200, `/health/ready` returns 503 until the
DB is migrated. In Compose we also have `depends_on: condition: service_healthy`, so the app only starts
after PostgreSQL passes its own `pg_isready` health check.

### Q: How can I look at the data myself?

```powershell
# psql inside the db container (no install needed)
docker compose exec db psql -U travelease -d travelease

  \dt                                  -- list tables
  SELECT COUNT(*) FROM "Customers";
  SELECT * FROM "__EFMigrationsHistory";
  \q
```

To use a GUI tool (DBeaver, pgAdmin) from Windows, uncomment `ports: - "5432:5432"` under `db:` in
docker-compose.yml, run `docker compose up -d`, and connect to `localhost:5432`.

---

## 7. How the database container gets created

### Q: We never wrote a Dockerfile for PostgreSQL. Where does it come from?
We use the **official image** `postgres:16-alpine` from Docker Hub. Compose pulls it automatically
the first time. All the setup logic is already inside that image.

### Q: What happens step by step on `docker compose up -d`?

```
 1. Compose reads docker-compose.yml and .env
 2. Creates network   travelease_default
 3. Creates volumes   travelease_pgdata, travelease_appdata    (only if they don't exist)
 4. Pulls postgres:16-alpine (first time only); builds travelease:1.0.0 (if needed)
 5. Starts container  travelease-db-1
      └─ the image's entrypoint script (docker-entrypoint.sh, inside the postgres image) checks:
         "Is /var/lib/postgresql/data empty?"
           YES (first run) -> initdb: creates the database cluster
                           -> creates user POSTGRES_USER with POSTGRES_PASSWORD
                           -> creates database POSTGRES_DB
                           -> runs any *.sql / *.sh in /docker-entrypoint-initdb.d/
           NO  (later runs) -> skips ALL of the above, just starts postgres
 6. Health check  pg_isready  every 5 s  -> container becomes "healthy"
 7. Only now starts   travelease-app-1   (depends_on: service_healthy)
 8. App migrates the schema and seeds sample data (section 6)
```

### Q: ⚠️ I changed `DB_PASSWORD` in `.env` and now the app can't log in. Why?
Because of step 5: `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` are used **only the first
time**, when the volume is empty. The old password is stored inside the `pgdata` volume. Options:

```powershell
# A) Development - throw away the database and start fresh (DELETES ALL DATA)
docker compose down -v
docker compose up -d

# B) Keep the data - change the password inside PostgreSQL to match .env
docker compose exec db psql -U travelease -d travelease -c "ALTER USER travelease PASSWORD 'new-password';"
```

### Q: Which OS / version is the database container?
Alpine Linux 3.24 with PostgreSQL 16.x (checked: `postgres (PostgreSQL) 16.16`). `PGDATA` (the data
folder) is `/var/lib/postgresql/data`.

---

## 8. Where is the data actually stored?

### Q: Summary table – what lives where?

| Data | Inside which container | Path inside container | Stored in | Survives `down`? | Survives `down -v`? |
|---|---|---|---|---|---|
| Customers, trips, bookings (DB rows) | db | `/var/lib/postgresql/data` | volume **travelease_pgdata** | ✅ | ❌ |
| Generated report files (CSV) | app | `/app/data/reports` | volume **travelease_appdata** | ✅ | ❌ |
| Data Protection keys (form security tokens) | app | `/app/data/keys` | volume **travelease_appdata** | ✅ | ❌ |
| App code (dll, wwwroot, appsettings) | app | `/app` | **image** layers | ✅ (in image) | ✅ (in image) |
| Logs | app, db | stdout / stderr | Docker's log files for the container | ❌ removed with container | ❌ |
| Anything else written in a container | any | anywhere else | container's writable layer | ❌ | ❌ |

### Q: What exactly is a volume?
A folder **managed by Docker**, outside the container's file system, mounted into the container at a
path. The container can be deleted and recreated; the volume stays and is mounted again.

```powershell
docker volume ls
docker volume inspect travelease_pgdata
#   "Mountpoint": "/var/lib/docker/volumes/travelease_pgdata/_data"
```

### Q: Where is that `/var/lib/docker/volumes/...` on my Windows laptop?
It is **inside Docker Desktop's Linux VM disk** (a virtual disk file managed by WSL2), **not** a normal
Windows folder. You are not supposed to edit it directly. To read or back up data, go **through
Docker**:

```powershell
# Back up the database to a SQL file on Windows
# (dump inside the container, then copy out - avoids PowerShell's ">" changing the file encoding)
docker compose exec db pg_dump -U travelease -d travelease -f /tmp/backup.sql
docker compose cp db:/tmp/backup.sql ./backup.sql

# Restore it
docker compose cp ./backup.sql db:/tmp/backup.sql
docker compose exec db psql -U travelease -d travelease -f /tmp/backup.sql

# Copy generated reports out of the app container
docker compose cp app:/app/data/reports ./reports-copy
```

### Q: Named volume vs bind mount?

| | Named volume (what we use) | Bind mount |
|---|---|---|
| Syntax | `pgdata:/var/lib/postgresql/data` | `./mydata:/var/lib/postgresql/data` |
| Location | managed by Docker | a folder you choose on the host |
| Best for | databases, production data | source code in development, config files |
| Permissions issues on Windows | rare | common (and slow for databases) |

### Q: What does `VOLUME ["/app/data"]` in the Dockerfile do?
It marks `/app/data` as a data folder. If you run the image **without** `-v`, Docker creates an
**anonymous** volume (random name) there automatically, so data is at least not written into the
container layer. In Compose we give it a proper name (`appdata`) so it's easy to find and keep.

### Q: Where do the logs go?
The app writes logs to **stdout** (information) and **stderr** (errors) – never to files. Docker
captures both. Read them with:

```powershell
docker compose logs -f app
docker compose logs db --tail 50
```

In Production the log lines are **JSON** (one object per line) so tools like ELK, Loki, Datadog or
CloudWatch can parse them. In Development they are plain single-line text.

---

## 9. Networking and ports

### Q: What does `ports: "8080:8080"` mean?
`"HOST_PORT:CONTAINER_PORT"`. Traffic arriving at your laptop's port 8080 is forwarded to port 8080
inside the app container. If 8080 is already in use on your laptop, set `APP_PORT=9090` in `.env` →
`"9090:8080"` → open http://localhost:9090. The **container** port stays 8080.

### Q: Then what does `EXPOSE 8080` in the Dockerfile do?
**Only documentation.** It does not open or publish anything. Publishing is done by `-p` / `ports:`.

### Q: Who decides that the app listens on 8080 inside the container?
The environment variable `ASPNETCORE_HTTP_PORTS=8080` (set in the Dockerfile and in Compose). Kestrel
listens on all interfaces (`http://+:8080`). Change it with `-e ASPNETCORE_HTTP_PORTS=5000`, then
publish `-p 8080:5000`.

### Q: Why is PostgreSQL's port 5432 not published?
It doesn't need to be. The app reaches it over the private Compose network. Not publishing it means
nobody outside Docker can attack the database. Publish it only temporarily for debugging tools.

### Q: How do the two containers find each other?
Compose creates the network `travelease_default` and attaches both containers. Docker runs a small
**built-in DNS server** on every user-defined network that answers "what is the IP of `db`?". IPs can
change on every restart; names don't – so always use names.

```powershell
docker network inspect travelease_default
```

### Q: HTTP only? What about HTTPS?
The container serves plain HTTP. In real deployments HTTPS is terminated **in front** of the container
by a reverse proxy / load balancer / Kubernetes ingress (Nginx, Traefik, Azure App Gateway, AWS ALB).
That is the standard pattern.

---

## 10. Running containers: users, logs, health, shutdown

### Q: Which user runs the app? Why does it matter?

```powershell
docker run --rm --entrypoint id travelease:1.0.0
# uid=1654(app) gid=1654(app) groups=1654(app)
```

The final image has `USER ${APP_UID}` (1654, a non-root user that comes with the .NET base images).
If an attacker breaks into the app, they are **not root** inside the container. The app files in
`/app` are owned by root, so the app can't even modify itself; only `/app/data` is writable.

Side effect: a non-root user can't listen on ports below 1024 – that's why .NET 8 images default to
**8080** instead of 80.

### Q: What are `/health`, `/health/live` and `/health/ready`?

| Endpoint | Question it answers | Checks | If it fails, the platform should... |
|---|---|---|---|
| `/health/live` | "Is the process alive?" | nothing external | **restart** the container |
| `/health/ready` | "Can it serve traffic right now?" | DB reachable + migrated, report folder writable | **stop sending traffic** (don't restart) |
| `/health` | Everything, for humans/dashboards | all checks | – |

The Dockerfile `HEALTHCHECK` calls `/health/ready` every 30 s; `docker ps` then shows
`(healthy)` / `(unhealthy)`. Kubernetes uses the same endpoints as liveness/readiness probes.

### Q: What happens on `docker stop`?
Docker sends **SIGTERM** to PID 1 → the app logs `shutting down (stop signal received)...`, finishes
in-flight requests (up to 20 s, configured in Program.cs), logs `stopped cleanly`. If it's still running
after Docker's timeout (10 s default), Docker sends **SIGKILL**. This only works properly because
`ENTRYPOINT ["dotnet", "TravelEase.dll"]` is in **exec form** – `dotnet` is PID 1. (In shell form,
`/bin/sh` is PID 1 and may not pass the signal on.)

### Q: What does `restart: unless-stopped` do?
If the container crashes or Docker Desktop restarts, Docker starts it again automatically – unless you
stopped it yourself with `docker stop` / `docker compose stop`.

---

## 11. Important files every DevOps engineer should know

### Docker & deployment files

| File | Purpose | When you touch it |
|---|---|---|
| [Dockerfile](../Dockerfile) | **The production image recipe** (3 stages: build → export → final) | changing base image, ports, user, health check |
| [docker-compose.yml](../docker-compose.yml) | Runs app + db together: env vars, volumes, ports, health, start order | adding a service, changing ports/volumes |
| [.dockerignore](../.dockerignore) | Files **not** sent to the build: `bin/`, `obj/`, `.env`, `.git/`, docs | when builds are slow or a secret could leak into the image |
| `.env` | **Your local secrets & settings** (DB password, ports). Never commit. | per machine / per environment |
| [.env.example](../.env.example) | Template of `.env` with safe dummy values – committed | when a new variable is added |
| [docker/docker-entrypoint.sh](../docker/docker-entrypoint.sh) | Start-up script used by `Dockerfile.instructions` (optional wait-for-DB, then `exec "$@"`) | custom start-up logic |
| [docker/Dockerfile.*](../docker/) | Learning variants: all 15 instructions, single-stage, pre-published, alpine | learning / comparison |
| [.gitattributes](../.gitattributes) | Forces LF line endings for `.sh` files (CRLF breaks shell scripts in Linux) | adding scripts |

### .NET application files

| File | Purpose | DevOps relevance |
|---|---|---|
| [TravelEase.csproj](../TravelEase.csproj) | Project file: target framework (`net8.0`), NuGet packages, version | `.NET` version must match the base image tag; changing it invalidates the restore cache layer |
| [Program.cs](../Program.cs) | Start-up: config, logging, DB, DI, health endpoints, HTTP pipeline | where health endpoints, logging and shutdown timeout are defined |
| [appsettings*.json](../appsettings.json) | Default configuration per environment | know the keys so you can override them with `__` env vars |
| [Configuration/DatabaseConnectionResolver.cs](../Configuration/DatabaseConnectionResolver.cs) | Reads `DB_*` / `ConnectionStrings__*` / `DB_PASSWORD_FILE` | which variables the app accepts |
| [Infrastructure/DatabaseInitializer.cs](../Infrastructure/DatabaseInitializer.cs) | Auto-migrate + seed with retries | `Database__ApplyMigrationsOnStartup`, `Database__SeedSampleData`, `Database__MaxStartupRetries` |
| [Migrations/](../Migrations/) | Database schema history (generated code) | schema changes ship with the image and apply on start-up |
| [HealthChecks/HealthChecks.cs](../HealthChecks/HealthChecks.cs) | What "healthy" and "ready" mean | probe configuration |
| [Infrastructure/HealthProbe.cs](../Infrastructure/HealthProbe.cs) | `dotnet TravelEase.dll healthcheck` – HTTP probe without curl | used by HEALTHCHECK |
| [Properties/launchSettings.json](../Properties/launchSettings.json) | Local `dotnet run` only | **not** used in containers |

### Environment variables cheat sheet

| Variable | Default | Meaning |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Which `appsettings.{Env}.json` loads; Dev shows detailed error pages |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port Kestrel listens on inside the container |
| `DB_HOST` / `DB_PORT` / `DB_NAME` / `DB_USER` | `localhost` / `5432` / `travelease` / `travelease` | DB location |
| `DB_PASSWORD` / `DB_PASSWORD_FILE` | – | DB password, or a file containing it (Docker/K8s secrets) |
| `ConnectionStrings__DefaultConnection` | empty | Full connection string (alternative to `DB_*`) |
| `Database__SeedSampleData` | `true` | Insert demo data into an empty DB |
| `Database__ApplyMigrationsOnStartup` | `true` | Run EF migrations at start-up |
| `App__CurrencySymbol` | `₹` | UI currency symbol |
| `Logging__LogLevel__Default` | `Information` | Log verbosity |

---

## 12. Quick-fire questions (interview style)

**Q: Difference between `CMD` and `ENTRYPOINT`?**
`ENTRYPOINT` = the fixed program; `CMD` = default arguments that can be replaced from `docker run`. We
use `ENTRYPOINT ["dotnet","TravelEase.dll"]`, so `docker run travelease:1.0.0 healthcheck` passes
`healthcheck` as an argument.

**Q: Difference between `COPY` and `ADD`?**
`COPY` just copies. `ADD` can also extract local `.tar.gz` archives and download URLs. Prefer `COPY`
unless you need extraction.

**Q: Difference between `ARG` and `ENV`?**
`ARG` exists only **during build** (e.g. `APP_VERSION`). `ENV` exists during build **and** in the
running container (e.g. `ASPNETCORE_ENVIRONMENT`).

**Q: Why multi-stage?**
Build with the big SDK image, ship only the small runtime image: 1.58 GB → 333 MB, no compilers or
source code in production.

**Q: What does `docker compose down -v` do?**
Removes containers and network **and volumes** → the database is deleted. Without `-v` the data stays.

**Q: What's the difference between `docker compose stop` and `down`?**
`stop` stops containers (they still exist, `start` resumes them). `down` removes them (volumes stay
unless `-v`).

**Q: The container exits immediately. How to debug?**
`docker compose logs app` (or `docker logs <container>`) to see the error, then
`docker run -it --rm --entrypoint sh travelease:1.0.0` to look around inside the image.

**Q: How do I get a shell inside a running container?**
`docker compose exec app sh` (or `bash`). For the db: `docker compose exec db sh`.

**Q: How do I rebuild after a code change?**
`docker compose up -d --build`. Only the changed layers rebuild.

**Q: Why is the first build slow and the second fast?**
First build downloads base images and NuGet packages; later builds reuse the **layer cache**.

**Q: Does the image contain the database?**
No. The image has only the app. The database is a separate container from a separate image, and its
data is in a volume.

**Q: Can I use the same image for Staging and Production?**
Yes – that's the goal ("build once, deploy many"). Only environment variables change.

**Q: How do I move this image to a server?**
Push to a registry (`docker tag travelease:1.0.0 myregistry.azurecr.io/travelease:1.0.0` →
`docker push ...`), then pull and run on the server. Or for offline transfer:
`docker save -o travelease.tar travelease:1.0.0` → copy file → `docker load -i travelease.tar`.

**Q: What is `.dockerignore` for if we already have `.gitignore`?**
`.gitignore` controls what goes into Git; `.dockerignore` controls what is sent as the **build
context** to Docker. Different tools, different files.

**Q: What's the "build context"?**
The folder you pass to `docker build` (the `.` at the end). Docker sends that folder (minus
`.dockerignore` entries) to the builder; `COPY` can only copy from it.

**Q: Why does Alpine sometimes break .NET apps?**
Alpine uses **musl** instead of glibc and doesn't include ICU (culture/globalisation data). Our
`Dockerfile.alpine` installs `icu-libs` to fix that.

**Q: Where is `docker` actually running on Windows?**
The `docker` CLI on Windows talks to the **Docker Engine** running inside Docker Desktop's Linux VM.

---

## 13. Troubleshooting: symptom → cause → fix

| Symptom | Likely cause | Fix |
|---|---|---|
| `http://localhost:8080` doesn't open | container not running or port taken | `docker compose ps`; change `APP_PORT` in `.env` |
| `required variable DB_PASSWORD is missing` | no `.env` file | `Copy-Item .env.example .env`, set a password |
| App log: `password authentication failed` | `.env` password changed after DB volume was created | section 7: `down -v` (dev) or `ALTER USER` |
| App log: `Connection refused` to `localhost:5432` | `DB_HOST` is `localhost` inside a container | use `db` (Compose) or `host.docker.internal` |
| `docker ps` shows `(unhealthy)` | `/health/ready` failing – usually DB | `docker compose logs app`; open `/health` in the browser |
| `/health/live` 200 but `/health/ready` 503 | app up, DB down / still migrating | wait, or check db container |
| Forms say "antiforgery token invalid" after restart | keys volume missing | keep the `appdata` volume mounted at `/app/data` |
| `exec /usr/local/bin/docker-entrypoint.sh: no such file or directory` (but the file exists!) | script saved with Windows CRLF line endings, so Linux looks for `/bin/bash\r` | save as LF (`.gitattributes` enforces this) |
| `permission denied` writing a file | app runs as non-root `app` user | write only under `/app/data` |
| Build re-downloads packages every time | `COPY . .` before `restore` | copy `.csproj` first, restore, then copy the rest |
| Image is huge | SDK shipped in final image | multi-stage build |
| `error during connect ... docker_engine` | Docker Desktop not started | start Docker Desktop, wait for "Engine running" |

---

## 14. More questions learners usually ask

These are the questions that typically come up during the first week of learning: in class, in a lab,
or in an interview.

### 14.1 Getting started

**Q: Do I need .NET or PostgreSQL installed on my laptop to run this project?**
No. You only need **Docker Desktop**. The SDK image compiles the app and the postgres image runs the
database. This project was built and tested on a laptop with **no .NET SDK installed**. You need the
.NET SDK only if you want to run or debug the app outside Docker (`dotnet run`).

**Q: Why not just install .NET and PostgreSQL directly on the server, like before?**
You can, but then every server has to be set up by hand, versions drift ("prod has 8.0.3, test has
8.0.10"), and two apps that need different versions conflict. With Docker the **image is the unit you
deploy**, and it runs the same way on a laptop, a test server and production.

**Q: What is the difference between a Dockerfile and docker-compose.yml?**

| Dockerfile | docker-compose.yml |
|---|---|
| How to **build one image** | How to **run one or more containers** together |
| Used by `docker build` | Used by `docker compose up` |
| Says what goes inside: OS, runtime, app files, user, port, start command | Says how to run it: env vars, ports, volumes, networks, start order, restart policy |
| One per image | One per application stack |

In short, the Dockerfile builds the image and the Compose file decides how it runs.

**Q: Docker Desktop vs Docker Engine – what is what?**
**Docker Engine** (`dockerd`) is the program that actually builds images and runs containers, and it
runs only on Linux. **Docker Desktop** is the Windows/Mac application that runs a Linux VM, puts Docker
Engine inside it, and adds a GUI and the CLI. Linux servers install just Docker Engine, without
Desktop. (Docker Desktop needs a paid licence for larger companies, so check your company's policy.)

**Q: `docker-compose` (with a hyphen) or `docker compose` (with a space)?**
`docker-compose` is the old standalone v1 tool, which is now retired. `docker compose` is v2, built into
the Docker CLI. Use the space version; the YAML file format is the same.

**Q: Can I run this on a Mac with an M1/M2/M3 chip?**
Yes. Microsoft's and PostgreSQL's images are **multi-architecture** (amd64 and arm64), so Docker pulls
the right one automatically. But an image you **build** on a Mac is arm64. To build one for a normal
x86 server, use `docker build --platform linux/amd64 ...`.

**Q: Can I run Windows containers instead?**
Docker Desktop can switch to "Windows containers" mode, and .NET has `nanoserver` images. This project
targets Linux containers, which are smaller, faster and what almost all cloud platforms use.

**Q: In what order should I learn this?**
1. Run it: `docker compose up -d` and open http://localhost:8080.
2. Read this FAQ.
3. Read [DOCKERFILE-EXPLAINED.md](DOCKERFILE-EXPLAINED.md) with the Dockerfile open next to it.
4. Do the exercises in [DOCKER-LABS.md](DOCKER-LABS.md).
5. Break things on purpose (stop the db, change the password, remove the volume) and read the logs.

### 14.2 Docker commands and flags

**Q: What do the common `docker run` flags mean?**

| Flag | Meaning | Example |
|---|---|---|
| `-d` | detached: run in the background | `docker run -d ...` |
| `-it` | interactive with a terminal, needed for a shell | `docker run -it --rm --entrypoint sh travelease:1.0.0` |
| `--rm` | delete the container automatically when it exits | one-off commands |
| `-p 8080:8080` | publish port host:container | |
| `-e NAME=value` | set an environment variable | `-e DB_HOST=db` |
| `--env-file .env` | load many env vars from a file | |
| `-v name:/path` | mount a volume | `-v travelease-data:/app/data` |
| `--name x` | give the container a fixed name | `--name travelease` |
| `--network n` | attach to a network | |
| `--entrypoint` | replace the image's ENTRYPOINT | `--entrypoint sh` |
| `--memory 512m --cpus 1` | resource limits | |

**Q: `docker run` vs `docker start` vs `docker exec`?**

| Command | What it does |
|---|---|
| `docker run` | **creates a new** container from an image and starts it |
| `docker start` | starts an **existing, stopped** container again |
| `docker exec` | runs an **extra** command inside an **already running** container, e.g. a shell |

**Q: Why does my container stop immediately after starting?**
A container lives only as long as its **main process (PID 1)** runs. If that process ends, because
the app crashed or the command finished (`docker run ubuntu` runs `bash`, which exits with no
terminal), the container stops. Check `docker logs <name>` and the exit code in `docker ps -a`.

**Q: What do the exit codes in `docker ps -a` mean?**

| Exit code | Usual meaning |
|---|---|
| `0` | finished normally |
| `1` | the application failed with an error (read the logs) |
| `137` | killed by SIGKILL, usually out of memory or `docker kill` / stop timeout |
| `139` | crashed (segmentation fault) |
| `143` | stopped by SIGTERM, a normal `docker stop` |

**Q: What happens to a running container when I rebuild the image?**
Nothing. The running container keeps using the old image. Only **new** containers use the new image,
so run `docker compose up -d --build`, which rebuilds and **recreates** the container.

**Q: Can I change code inside the container and see the change?**
Not in this setup. The image contains **compiled** code, so you edit on your laptop and rebuild. For
fast development loops, teams use a bind mount of the source plus `dotnet watch` in an SDK
container, or simply run `dotnet run` locally against the PostgreSQL container.

**Q: What does `docker inspect` show?**
Everything about a container or image, in JSON: environment variables, mounts, networks, IP address,
health-check history, labels, the start command. For example:

```powershell
docker inspect travelease-app-1 --format "{{json .State.Health}}"
docker inspect travelease:1.0.0 --format "{{json .Config.Labels}}"
```

**Q: How do I see CPU and memory usage?**
`docker stats` (live, like Task Manager).

### 14.3 Images, tags and disk space

**Q: What is the difference between image ID, tag and digest?**

| | Example | Properties |
|---|---|---|
| Tag | `travelease:1.0.0` | a human-friendly **movable** label; it can be pointed at a different image later |
| Image ID | `sha256:3f2a...` (short form `3f2a1b...`) | identifies the image locally |
| Digest | `postgres@sha256:9c1e...` | a content hash in the registry; **never changes**; the most exact reference |

**Q: Why should I avoid the `latest` tag?**
`latest` is just the default tag name; it does not mean "newest". Today `latest` might be version 1.4
and tomorrow 2.0 with breaking changes, so you can't tell what is running or roll back reliably. Always
use explicit versions like `travelease:1.0.0`.

**Q: How do I release version 1.0.1 and roll back if it is broken?**

```powershell
# release
# (set APP_VERSION=1.0.1 in .env, then:)
docker compose up -d --build          # builds travelease:1.0.1, replaces the container

# roll back: set APP_VERSION=1.0.0 in .env again
docker compose up -d                  # the old image still exists, so the switch is instant
```

The database is **not** rolled back automatically. A migration that changed the schema stays applied,
which is why schema changes should be backward compatible.

**Q: What are `<none>` images?**
These are **dangling** images: old builds whose tag moved to a newer build. They only use disk space.

**Q: Docker is eating my disk. How do I clean up?**

```powershell
docker system df                 # what uses space
docker image prune               # remove dangling images
docker builder prune             # remove build cache
docker container prune           # remove stopped containers
docker system prune              # all of the above (asks first)
docker volume prune              # unused volumes  ⚠️ deletes data of removed stacks
```

Never run `docker volume prune` casually: it can delete database volumes.

**Q: Where are images stored on my laptop?**
Inside Docker Desktop's Linux VM disk (a `.vhdx` virtual-disk file used by WSL2), not as normal files.
Manage them with `docker image ls` / `docker rmi`. To change where that disk file lives, use
Docker Desktop → Settings → Resources.

### 14.4 Docker Compose

**Q: What do `${DB_PASSWORD:?...}` and `${APP_PORT:-8080}` mean in docker-compose.yml?**

| Syntax | Meaning |
|---|---|
| `${VAR}` | value from `.env` or the shell |
| `${VAR:-default}` | use `default` if VAR is empty or missing |
| `${VAR:?message}` | **stop with an error** if VAR is missing. We use it so nobody runs without a password |
| `$${VAR}` | a literal `$`, passed to the container's shell (used in the `pg_isready` health check) |

**Q: `.env` vs `environment:` vs `env_file:` – what's the difference?**
- **`.env`** is read by **Compose itself**, to fill in `${...}` placeholders in the YAML. It is not
  automatically passed into containers.
- **`environment:`** lists variables that **go into the container**.
- **`env_file:`** loads a file's variables **into the container**.

We use `.env` + `environment:`, so it's explicit exactly which variables each container receives.

**Q: Does `depends_on` make the app wait until PostgreSQL is ready?**
Only with `condition: service_healthy`, which is what we use. Plain `depends_on: [db]` waits only for
the db container to **start**, not for PostgreSQL to accept connections. That's also why the app
retries the connection by itself; you need both.

**Q: If the app becomes `unhealthy`, does Docker restart it?**
**No, and this surprises everyone.** Plain Docker and Compose only **report** `unhealthy`. `restart:`
reacts only when the process **exits**. Automatic restart on failed health checks is a feature of
orchestrators (Kubernetes liveness probes, Docker Swarm, ECS, Azure Container Apps).

**Q: Can I run 3 copies of the app?**
`docker compose up -d --scale app=3` fails here with "port is already allocated", because all three
copies want host port 8080. Real scaling uses a load balancer in front (Nginx/Traefik) and no fixed
host port on the app, or an orchestrator like Kubernetes.

**Q: What is the project name `travelease` used for?**
`name: travelease` at the top of the Compose file is the **prefix** for everything Compose creates:
containers `travelease-app-1`, network `travelease_default`, volumes `travelease_pgdata`. Two copies of
the project with different names don't clash.

**Q: `docker compose up` without `-d`?**
It runs in the foreground and shows the logs of all containers mixed together; Ctrl+C stops them all.
That's handy for a first run, but `-d` plus `docker compose logs -f` is the normal way.

### 14.5 Database and data

**Q: Why PostgreSQL and not MySQL or SQL Server?**
PostgreSQL is free, open source, very capable, and available as a managed service on every cloud
(Azure Database for PostgreSQL, AWS RDS, Google Cloud SQL). EF Core supports all of them; to switch,
you would change the NuGet provider package and the `UseNpgsql(...)` line.

**Q: Why is the database image Alpine but the app image Debian?**
Each image is chosen on its own. `postgres:16-alpine` is the official small variant. For .NET, the
Debian-based image is Microsoft's default and the most compatible. Alpine also works for .NET (see
`docker/Dockerfile.alpine`, 224 MB) but needs extra care with ICU and globalisation.

**Q: Should a production database run in a container at all?**
That's a debated point. For development, testing and small systems, a container plus a volume is fine. Many
companies run production databases on a **managed cloud service** instead, so backups, high
availability, patching and point-in-time restore are handled for them. The app image doesn't change:
just set `DB_HOST` to the cloud server.

**Q: Is it safe that the app migrates the database automatically on start-up?**
It's convenient and fine for one instance. With **several instances** starting at the same time, they
could all try to migrate at once (EF Core 8 has no built-in migration lock). In larger production
setups, migrations run as a **separate step** in the deployment pipeline (`dotnet ef database update`
or an EF "migration bundle"), and the app is started with `Database__ApplyMigrationsOnStartup=false`.

**Q: What timezone does the container use? Dates look off by 5:30.**
Containers default to **UTC**. The app stores all timestamps in UTC, which is the best practice, so
an India-based user sees the time 5:30 behind if it's shown raw. Convert to the user's time zone
when displaying. Avoid changing the container's timezone just to "fix" the display.

**Q: Does stopping Docker Desktop or restarting the laptop delete my data?**
No. Volumes are on the VM disk and survive restarts. Only `docker compose down -v`, `docker volume rm`
or `docker volume prune`, or resetting or uninstalling Docker Desktop deletes them.

**Q: Can two containers share one volume?**
Yes, technically, but two **database** servers must never use the same data folder: that corrupts it.
Sharing is fine for read-mostly data like static files.

### 14.6 The ASP.NET Core application itself

**Q: What is MVC?**
**M**odel–**V**iew–**C**ontroller is a way to split a web app into parts:

| Part | In this project | Job |
|---|---|---|
| Model | `Models/Customer.cs`, `Trip.cs`, `Booking.cs` | the data and its rules |
| View | `Views/Customers/Index.cshtml` (Razor = HTML + C#) | what the user sees |
| Controller | `Controllers/CustomersController.cs` | receives the request, calls services, picks the view |

We also have a **Services** layer (`Services/`), so controllers stay thin and the business logic
(for example, "don't overbook seats") sits in one place.

**Q: What is EF Core and why not just write SQL?**
EF Core is an **ORM** (Object–Relational Mapper). You write C# LINQ such as
`db.Customers.Where(c => c.City == "Pune")` and it generates the SQL, maps rows to C# objects, tracks
changes and creates migrations. It also **parameterises** values, which protects against SQL
injection. Raw SQL is still possible when needed.

**Q: What is dependency injection (DI) and what do Singleton / Scoped mean?**
Instead of a controller doing `new CustomerService(new DbContext(...))`, it declares
`ICustomerService` in its constructor and the framework supplies it. The registrations are in
[Program.cs](../Program.cs):

| Lifetime | One instance per... | Used here for |
|---|---|---|
| Singleton | whole application | `DatabaseState`, `UiFormatter`, `ReportFileService` |
| Scoped | HTTP request | `DbContext`, `CustomerService`, `BookingService`, ... |
| Transient | every time it is asked for | (not used) |

**Q: What is Kestrel? Do we need IIS or Nginx?**
Kestrel is the fast web server built into ASP.NET Core, and it is what listens on port 8080 in the
container. On Linux there is no IIS (`web.config` is ignored). Nginx or a cloud load balancer is
optional, placed **in front** for HTTPS, domains and load balancing.

**Q: What is `wwwroot`?**
The folder for **static files** that are served as-is: CSS, JavaScript, images. `/css/site.css` in the
browser maps to `wwwroot/css/site.css`.

**Q: What is the difference between the website pages and the `/api/...` endpoints?**
Pages (`/customers`) return **HTML** for humans. API endpoints (`/api/customers`) return **JSON** for
other programs (mobile apps, scripts, Postman). Both use the same services and database.

**Q: The log says `Listening on: http://[::]:8080`. What is `[::]`?**
It means "all network interfaces" (IPv6 notation that also covers IPv4), like `0.0.0.0`. That's
**required** in a container. If an app listens only on `localhost` / `127.0.0.1` **inside** the
container, port mapping can't reach it, and the browser shows "connection reset" even though the app
runs.

**Q: What are the antiforgery tokens and "Data Protection keys"?**
Every form contains a hidden token that proves the form came from our site; this protects against CSRF
attacks. The tokens are signed with keys stored in `/app/data/keys`. If those keys were lost on
every restart, forms opened before the restart would fail. That's why they're on a volume.

### 14.7 Security

**Q: Can anyone see the database password?**
Anyone who can run `docker inspect` on the container can see its environment variables, including
`DB_PASSWORD`. That's acceptable on your own laptop. In production use **secrets**: Docker/Kubernetes
secrets mounted as a file, plus `DB_PASSWORD_FILE=/run/secrets/db_password`, or a cloud secret store
(Azure Key Vault, AWS Secrets Manager).

**Q: Is it OK to put a password in the Dockerfile with `ENV` or `ARG`?**
**Never.** It is stored in the image metadata and history forever (`docker history`, `docker inspect`),
and anyone who pulls the image can read it, even if a later layer "deletes" it. Secrets are given
**at run time**, never at build time.

**Q: Why run as non-root if the container is isolated anyway?**
Isolation is good but not perfect. If an attacker exploits the app, root inside the container makes
escaping or damaging mounted data much easier. Non-root is a cheap, standard extra layer of defence,
and many Kubernetes clusters refuse to run root containers.

**Q: How do I know if my image has vulnerabilities?**
Scan it: `docker scout cves travelease:1.0.0` (built into Docker Desktop), or Trivy / Snyk in CI.
Most findings come from the **base image**, so rebuild regularly to pick up patched base images.

**Q: Why are `.env` and `.git` in `.dockerignore`?**
So secrets and source history are never sent to the builder and can't accidentally be copied into an
image with `COPY . .`.

### 14.8 Going to production (CI/CD, cloud, Kubernetes)

**Q: How would this fit into a CI/CD pipeline?**

```
 git push ──▶ CI (GitHub Actions / Azure DevOps / Jenkins / GitLab CI)
                1. docker build -t myregistry/travelease:<version> .
                2. run tests, scan the image (Trivy / Docker Scout)
                3. docker push myregistry/travelease:<version>
          ──▶ CD
                4. deploy that exact tag to Dev ─▶ Staging ─▶ Production
                   (same image everywhere; only env vars/secrets differ)
```

**Q: What is Kubernetes and how does it relate to Docker?**
Docker runs containers on **one machine**. Kubernetes (K8s) runs and manages containers across **many
machines**: it restarts failed containers, scales replicas, does rolling updates and load balancing.
The **same image** works there. Compose concepts map roughly like this:

| Compose | Kubernetes |
|---|---|
| service `app` | Deployment + Service |
| `environment:` | ConfigMap / Secret |
| volume | PersistentVolumeClaim |
| `ports:` | Service / Ingress |
| healthcheck | livenessProbe → `/health/live`, readinessProbe → `/health/ready` |

**Q: Where can this image be hosted in the cloud?**
Any container platform works: Azure Container Apps / App Service / AKS, AWS ECS / App Runner / EKS,
Google Cloud Run / GKE, or a plain VM with Docker. They all need the same three things this app already
supports: a port (`ASPNETCORE_HTTP_PORTS`), environment variables for config, and health endpoints.

**Q: How are logs collected in production if they only go to stdout?**
The platform collects container stdout/stderr automatically (Azure Monitor, CloudWatch, Fluent Bit →
Elasticsearch/Loki). Because our production logs are **JSON**, fields like level, message and
trace ID can be searched without extra parsing. Writing log files inside containers is avoided.

**Q: How much memory/CPU does the container get?**
By default, as much as the Docker VM has. Set limits with `--memory 512m --cpus 1` (or `deploy.resources`
in Compose, or `resources.limits` in Kubernetes). .NET reads these limits and adapts garbage
collection and thread counts automatically.

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Base image** | The image a Dockerfile starts `FROM` – provides the OS files and runtimes |
| **Layer** | A cached file-system diff created by one Dockerfile instruction |
| **Build context** | The folder sent to Docker during `docker build` |
| **Registry** | Server that stores images (Docker Hub, MCR, ACR, ECR, GHCR) |
| **Volume** | Docker-managed persistent storage mounted into containers |
| **Bind mount** | A host folder mounted into a container |
| **Kestrel** | The web server built into ASP.NET Core |
| **EF Core** | Entity Framework Core – the ORM that turns C# objects/LINQ into SQL |
| **Npgsql** | The .NET driver for PostgreSQL |
| **Migration** | Versioned C# description of a database schema change |
| **Seeding** | Inserting initial / sample data |
| **DI (Dependency Injection)** | The framework creates and passes in services (`ICustomerService`) instead of code using `new` |
| **NuGet** | .NET package manager (like npm for Node, pip for Python) |
| **IL / JIT** | .NET compiles to Intermediate Language; the runtime JIT-compiles it to machine code |
| **SIGTERM / SIGKILL** | "Please stop" vs "stop now" signals sent by `docker stop` |
| **PID 1** | The first process in a container; receives signals; container lives as long as it does |
| **Liveness / Readiness** | "Is it alive?" vs "Can it take traffic?" health checks |
| **WSL2** | Windows Subsystem for Linux – runs the Linux VM that Docker Desktop uses |
| **glibc / musl** | C standard libraries used by Debian/Ubuntu vs Alpine |
