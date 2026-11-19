# TravelEase: the Dockerfile and the database container, explained from scratch

**Contents**

1. [Basic ideas](#part-1--basic-ideas)
2. [What the app needs in order to run](#part-2--what-the-app-needs-in-order-to-run)
3. [The Dockerfile, line by line](#part-3--the-dockerfile-line-by-line)
4. [What happens during `docker build`](#part-4--what-happens-during-docker-build)
5. [How the database container is created](#part-5--how-the-database-container-is-created-docker-composeyml)
6. [What happens when you run `docker compose up`](#part-6--what-happens-step-by-step-when-you-run-docker-compose-up--d---build)
7. [Without multi-stage: only the final stage](#part-7--without-multi-stage-a-dockerfile-with-only-the-final-stage)
8. [Running everything with plain `docker run`](#part-8--running-everything-with-plain-docker-run-no-compose)
9. [Command cheat sheet](#part-9--command-cheat-sheet)
10. [Common questions](#part-10--common-questions)

> Run all commands from the `TravelEase` folder, the one that contains the `Dockerfile`.

---

## Part 1 – Basic ideas

| Term | Meaning |
|---|---|
| **Dockerfile** | A text file of instructions (`FROM`, `COPY`, `RUN`, …) that describes how to build an image. Think of it as a **recipe**. |
| **Image** | The result of building a Dockerfile: a read-only package with an OS base, the .NET runtime, our app files and default settings. Think of it as the **cake** made from the recipe. Images have a name and a tag, for example `travelease:1.0.0`. |
| **Container** | A **running instance** of an image. One image can start many containers, just as one class can create many objects. Each container has its own file system, processes, network address and host name. |
| **Layer** | Every instruction that changes files (`COPY`, `RUN`, `ADD`) adds a layer. Docker **caches** layers: when an instruction and its inputs haven't changed, the old layer is reused. That is why rebuilds are fast. |
| **Volume** | Storage that lives **outside** the container. Deleting a container deletes its own files, but a volume survives. We use volumes for the database files and the generated reports. |
| **Network** | Containers on the same Docker network can reach each other by **container or service name**. Our app reaches the database at the host name `db`. |

```
Dockerfile ──(docker build)──▶ Image ──(docker run)──▶ Container
 (recipe)                      (cake)                  (running app)
```

---

## Part 2 – What the app needs in order to run

TravelEase is an ASP.NET Core application. Before it can run, it has to be:

1. **Restored**: its NuGet packages (Entity Framework, Npgsql, …) are downloaded.
2. **Compiled**: the C# and Razor files are turned into `TravelEase.dll`.
3. **Published**: the DLLs, `wwwroot` (CSS/JS/images) and `appsettings*.json` are collected into one folder that is ready to deploy.

Steps 1–3 need the **.NET SDK**, the full toolkit with compilers (~800 MB).
Running the published output needs only the **ASP.NET Core runtime**, which is much smaller.

At run time the app also needs:

- a **PostgreSQL database** (host, port, database name, user, password)
- a **writable folder** for report files
- an **HTTP port** to listen on (8080)

This is why our Dockerfile has **two main stages**:

| Stage | Base image | Job |
|---|---|---|
| `build` | SDK image | restore, compile, publish |
| `final` | runtime image | only *runs* the published output |

---

## Part 3 – The Dockerfile, line by line

### Header

```dockerfile
# syntax=docker/dockerfile:1
```

This tells Docker which Dockerfile syntax version to use, and it must be the very first line. Every other line that starts with `#` is a comment.

```dockerfile
ARG DOTNET_VERSION=8.0
```

**`ARG`** declares a **build-time** variable, here with the default value `8.0`. Because it comes *before* the first `FROM`, it can be used inside `FROM` lines. You can change it without editing the file:

```bash
docker build --build-arg DOTNET_VERSION=8.0 .
```

ARG values exist only while the image is being built. They are **not** available to the running application; that is what `ENV` is for.

### Stage 1: `build`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
```

**`FROM`** chooses the starting image, and every stage begins with one.
`mcr.microsoft.com/dotnet/sdk:8.0` is Microsoft's official image with the .NET 8 SDK.
`AS build` names this stage so later stages can refer to it.

```dockerfile
ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=1.0.0
```

These are more build-time variables, declared inside the stage.
**Rule:** an `ARG` must be declared in each stage that uses it.

- `BUILD_CONFIGURATION`: `Release` (optimised) or `Debug`
- `APP_VERSION`: the version compiled into the app and shown in the UI

```dockerfile
WORKDIR /src
```

This creates `/src` (if needed) and makes it the current folder for every following `COPY` and `RUN`. It works like `mkdir /src && cd /src`.

```dockerfile
COPY TravelEase.csproj ./
RUN dotnet restore TravelEase.csproj
```

- **`COPY`** copies files from your computer (the *build context*) into the image.
- **`RUN`** executes a command inside the image while it is being built.

> **Why copy only the `.csproj` first? Layer caching.**
> The `.csproj` lists the NuGet packages, and `dotnet restore` (downloading them) is slow. If `TravelEase.csproj` hasn't changed, Docker reuses the cached restore layer, so editing a `.cs` or `.cshtml` file does **not** download the packages again.
>
> | You change… | What gets rebuilt |
> |---|---|
> | a `.cshtml` / `.cs` file | only the layers after `COPY . .` (fast) |
> | `TravelEase.csproj` | the restore runs again (slower) |

```dockerfile
COPY . .
```

This copies the rest of the source (Controllers, Views, wwwroot, …) into `/src`. The first `.` is the current folder on your computer and the second `.` is the current folder in the image (`/src`).
The **`.dockerignore`** file decides what is *not* copied: `bin/`, `obj/`, `.env` (secrets), `.git`, and so on. That keeps the image clean and the build fast.

```dockerfile
RUN dotnet publish TravelEase.csproj \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:Version="${APP_VERSION}"
```

This compiles the app and writes the deployable output to `/app/publish`.

| Option | Meaning |
|---|---|
| `--configuration Release` | optimised build |
| `--no-restore` | the packages were already restored in the earlier layer |
| `--output /app/publish` | where the output goes |
| `-p:UseAppHost=false` | no native launcher; we start the app with `dotnet X.dll` |
| `-p:Version=...` | stamps the version into `TravelEase.dll` |

A `\` at the end of a line continues the command on the next line.
After this step, `/app/publish` contains `TravelEase.dll`, `wwwroot/`, `appsettings*.json`, …

### Stage 2: `export` (optional helper)

```dockerfile
FROM scratch AS export
COPY --from=build /app/publish /
```

`scratch` is a **completely empty** image, so this stage holds only the published files. A normal build doesn't use it. It exists so you can pull the published files out to your computer **without installing the .NET SDK**:

```bash
docker build --target export --output publish .
```

This creates a `publish` folder next to the Dockerfile, which [Part 7](#part-7--without-multi-stage-a-dockerfile-with-only-the-final-stage) uses.

### Stage 3: `final` (the image we actually run)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
```

This is a **new, clean** starting point: the ASP.NET Core **runtime** image (Debian Linux + .NET runtime). It has **no** SDK, compilers or source code, and nothing from stage 1 comes along unless we copy it explicitly. That is the whole point of a multi-stage build:

```
stage "build"  (SDK, source, NuGet cache)  ......... thrown away  (~1.5 GB)
stage "final"  (runtime + published files only) ... shipped      (~330 MB)
```

```dockerfile
ARG APP_VERSION=1.0.0
ARG BUILD_DATE=unknown
ARG VCS_REF=unknown
```

This is a new stage, so the ARGs must be declared again (same rule as before).

```dockerfile
LABEL org.opencontainers.image.title="TravelEase" \
      org.opencontainers.image.version="${APP_VERSION}" ...
```

**`LABEL`** attaches **metadata** to the image as key/value pairs: title, version, owner, build date. It does not change how the app runs. The `org.opencontainers.image.*` keys are an industry standard (OCI). To view the labels:

```bash
docker inspect --format "{{json .Config.Labels}}" travelease:1.0.0
```

```dockerfile
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__Version=${APP_VERSION} \
    Storage__ReportsPath=/app/data/reports \
    Storage__DataProtectionKeysPath=/app/data/keys \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
```

**`ENV`** sets environment variables that are **saved in the image** and visible to the running app. They are defaults, and each one can be overridden when the container starts: `docker run -e ASPNETCORE_ENVIRONMENT=Staging ...`

| Variable | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Development / Staging / Production |
| `ASPNETCORE_HTTP_PORTS` | the port the app listens on *inside* the container |
| `App__Version` | shown in the UI; it takes its value from the ARG, turning a build-time value into a run-time value |
| `Storage__ReportsPath` | where exported CSV reports are written |

A double underscore `__` means a nested setting: `App__Version` is the same as `"App": { "Version": ... }` in `appsettings.json`.

> **ARG vs ENV in one line:** `ARG` exists only while building the image; `ENV` also exists while the container runs.

```dockerfile
WORKDIR /app
```

The app lives in `/app` inside the container.

```dockerfile
RUN mkdir -p /app/data/reports /app/data/keys \
    && chown -R "${APP_UID}:${APP_UID}" /app/data
```

This creates the data folders and gives ownership to the non-root user.
`APP_UID` is already provided by Microsoft's .NET 8 images: it is `1654`, the ID of a built-in user named **`app`**. This line still runs as root, because we haven't switched user yet.

```dockerfile
COPY --from=build /app/publish .
```

**This is the key line of a multi-stage build.** `--from=build` means "copy from the stage named `build`", not from your computer, and only the published output is taken: no SDK, no source code, no leftovers.
The files are owned by root, so the app user can read them but cannot modify them. That is a security benefit.

```dockerfile
USER ${APP_UID}
```

From here on, and whenever the container runs, processes use UID 1654 (`app`), **not root**. If an attacker ever broke into the app, they would not have root rights. To check: `docker exec <container> id` prints `uid=1654(app)`.

```dockerfile
VOLUME ["/app/data"]
```

This declares that `/app/data` holds data which must **outlive the container** (reports and security keys). If you don't mount a volume, Docker creates an anonymous one. Normally you name it:

```bash
docker run -v travelease-data:/app/data ...
```

```dockerfile
EXPOSE 8080
```

**This is documentation only.** It says "this app listens on 8080" but does **not** open the port to your computer. You publish the port when you run the container:

```bash
docker run -p 8080:8080 ...     # HOST port : CONTAINER port
```

For example, `-p 5000:8080` means you browse to `http://localhost:5000` and Docker forwards the traffic to port 8080 inside the container.

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "TravelEase.dll", "healthcheck", "/health/ready"]
```

Docker runs this command inside the container every 30 seconds. Exit code **0 = healthy**, **1 = unhealthy**.

| Option | Meaning |
|---|---|
| `--interval=30s` | how often to check |
| `--timeout=5s` | a check slower than 5 s counts as failed |
| `--start-period=30s` | failures during the first 30 s don't count, because the app is still starting |
| `--retries=3` | 3 failures in a row mark the container `unhealthy` |

The usual approach is `curl http://localhost/...`, but the runtime image has **no curl**. So the app has a small built-in probe: `dotnet TravelEase.dll healthcheck` calls `http://127.0.0.1:8080/health/ready` and exits 0 or 1. `/health/ready` also checks that the **database** is reachable.

To see the status, look at the STATUS column of `docker ps`, or run:

```bash
docker inspect --format "{{.State.Health.Status}}" <container>
```

```dockerfile
ENTRYPOINT ["dotnet", "TravelEase.dll"]
```

This is the command executed when the container starts: run the app with the .NET runtime.

- The JSON-array form (**exec form**) makes `dotnet` process number 1 in the container. `docker stop` sends it the stop signal directly, so the app shuts down cleanly and you see "stopped cleanly" in the logs.
- Anything written after the image name in `docker run` is **added** to the entrypoint:

  ```bash
  docker run travelease:1.0.0 --urls http://+:9090
  # runs: dotnet TravelEase.dll --urls http://+:9090
  ```

> **CMD vs ENTRYPOINT** (practised with `docker/Dockerfile.instructions`)
>
> | | Role | How to replace it |
> |---|---|---|
> | `ENTRYPOINT` | the fixed program | `--entrypoint` |
> | `CMD` | the default arguments or command | anything written after the image name |

---

## Part 4 – What happens during `docker build`

```bash
docker build -t travelease:1.0.0 --build-arg APP_VERSION=1.0.0 .
#            └─ image name:tag   └─ build argument            └─ build context (this folder)
```

1. Docker reads `.dockerignore` and sends the remaining files (the **build context**) to the build engine.
2. **Stage `build`**: pulls the SDK image, restores the packages, publishes to `/app/publish`.
3. **Stage `export`** is skipped, because nothing needs it.
4. **Stage `final`**: pulls the runtime image, sets the labels and env vars, creates the folders, copies `/app/publish` from stage `build`, switches the user, and so on.
5. **Only the final stage** becomes the image `travelease:1.0.0`. Stage `build` stays only in the build cache.

To check the result:

```bash
docker images travelease               # size ~333 MB
docker image history travelease:1.0.0  # one row per layer
```

---

## Part 5 – How the database container is created (`docker-compose.yml`)

We do **not** write a Dockerfile for the database. We use the **official PostgreSQL image** from Docker Hub (`postgres:16-alpine`). Docker Compose describes **both** containers in one file:

```yaml
services:
  db:
    image: postgres:16-alpine              # ready-made image, nothing to build
    environment:
      POSTGRES_DB: travelease              # database to create
      POSTGRES_USER: travelease            # user to create
      POSTGRES_PASSWORD: ${DB_PASSWORD}    # read from the .env file
    volumes:
      - pgdata:/var/lib/postgresql/data    # where PostgreSQL stores its files
    healthcheck:
      test: pg_isready -U ... -d ...       # "is PostgreSQL accepting connections?"

  app:
    build: .                               # build OUR Dockerfile
    depends_on:
      db:
        condition: service_healthy         # start the app only after db is healthy
    environment:
      DB_HOST: db                          # the database's host name = service name
      DB_PASSWORD: ${DB_PASSWORD}          # same password as the db service
    ports:
      - "8080:8080"                        # browser -> localhost:8080 -> container 8080
    volumes:
      - appdata:/app/data                  # reports + keys survive restarts

volumes:
  pgdata:                                  # named volumes, created automatically
  appdata:
```

### How the database itself gets created

1. **First start, with an empty `pgdata` volume.**
   The postgres image's own start-up script sees the empty data folder and:
   - initialises a new PostgreSQL cluster
   - creates the user `POSTGRES_USER` with the password `POSTGRES_PASSWORD`
   - creates the database `POSTGRES_DB`

   At this point the database exists, but it has **no tables**.

2. **The tables are created by our app, not by Docker.**
   When TravelEase starts, its `DatabaseInitializer` runs the **Entity Framework migrations** (the `Migrations/` folder). They create the `Customers`, `Trips` and `Bookings` tables, with their keys, indexes and relationships. Then `SampleDataSeeder` inserts 40 customers, 36 trips and about 220 bookings, but only if the tables are empty.

3. **Later starts, when `pgdata` already has data.**
   PostgreSQL skips initialisation and **ignores** the `POSTGRES_*` variables. The app sees that the migrations are applied and the data exists, so nothing is re-created and your data is kept.

> ⚠️ **Important:** changing `DB_PASSWORD` after the first start does **not** change the database password, so you get *"password authentication failed"*. To reset everything:
>
> ```bash
> docker compose down -v   # -v deletes the volumes = deletes all data!
> ```

### How the app finds the database

Compose creates a private network (`travelease_default`) and joins both containers to it. On that network, Docker provides DNS, so the name **`db`** resolves to the database container. The app builds its connection string from the environment variables:

```
DB_HOST=db  DB_PORT=5432  DB_NAME=travelease  DB_USER=travelease  DB_PASSWORD=...
   └─▶  Host=db;Port=5432;Database=travelease;Username=travelease;Password=...
```

The code is in `Configuration/DatabaseConnectionResolver.cs`, and no password is written in the code.

> Inside a container, `localhost` means **that container itself**. That is why `DB_HOST` must be `db`, not `localhost`.

```
Your computer
┌──────────────────────────────────────────────────────────────────┐
│  Browser ── http://localhost:8080                                │
│                 │  (port mapping 8080:8080)                      │
│  ┌──────────── Docker network: travelease_default ─────────────┐ │
│  │              ▼                                              │ │
│  │   ┌──────────────────────┐   Host=db:5432   ┌─────────────┐ │ │
│  │   │ app (TravelEase)     │ ───────────────▶ │ db          │ │ │
│  │   │ user: app, port 8080 │                  │ PostgreSQL  │ │ │
│  │   └──────────┬───────────┘                  └──────┬──────┘ │ │
│  └──────────────┼─────────────────────────────────────┼────────┘ │
│       volume appdata                       volume pgdata         │
│       (/app/data: reports, keys)   (/var/lib/postgresql/data)    │
└──────────────────────────────────────────────────────────────────┘
```

---

## Part 6 – What happens, step by step, when you run `docker compose up -d --build`

1. Compose reads `docker-compose.yml` and `.env` (`DB_PASSWORD` etc.).
2. It **builds** the app image from the Dockerfile (see [Part 4](#part-4--what-happens-during-docker-build)).
3. It creates the **network** `travelease_default` and the **volumes** `pgdata` and `appdata`.
4. It starts the **db** container. PostgreSQL initialises (on the first run only).
5. The db health check (`pg_isready`) runs every 5 s until it reports **healthy**.
6. Because of `depends_on: condition: service_healthy`, **only now** is the app started.
7. The app logs `Starting TravelEase...` and listens on port 8080.
8. In the background, the app applies the **migrations** and **seeds** the sample data.
9. `/health/ready` turns healthy, and the app shows `(healthy)` in `docker compose ps`.
10. Open **http://localhost:8080** in the browser.

To watch it happen:

```bash
docker compose up -d --build
docker compose ps            # STATUS column
docker compose logs -f app   # Ctrl+C to stop following
```

---

## Part 7 – Without multi-stage: a Dockerfile with only the final stage

A multi-stage build compiles the app **inside** Docker. If you want a Dockerfile with **only the final (runtime) stage**, the compiling must happen **before** `docker build`, outside the Dockerfile, and the Dockerfile then just copies the ready-made files in.

> This is common in CI/CD pipelines, where one job compiles and tests and the next job only packages.

### Step 1 – Produce the published files in a `publish` folder

Choose **one** option.

**Option A:** with the .NET 8 SDK installed on your computer

```bash
dotnet publish TravelEase.csproj -c Release -o publish -p:UseAppHost=false
```

**Option B:** without installing the SDK. Docker compiles the app once and hands you the files, using the `export` stage of the main Dockerfile:

```bash
docker build --target export --output publish .
```

Either way, you end up with `TravelEase/publish/TravelEase.dll`, `wwwroot/`, …

### Step 2 – The final-stage-only Dockerfile

This file already exists as [`docker/Dockerfile.published`](../docker/Dockerfile.published):

```dockerfile
# Only ONE FROM = only one stage. Runtime image, no SDK.
FROM mcr.microsoft.com/dotnet/aspnet:8.0

LABEL org.opencontainers.image.title="TravelEase"

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Storage__ReportsPath=/app/data/reports \
    Storage__DataProtectionKeysPath=/app/data/keys

WORKDIR /app

# Data folder for the non-root user
RUN mkdir -p /app/data/reports /app/data/keys && chown -R "${APP_UID}:${APP_UID}" /app/data

# The ONLY real difference: copy from the HOST folder "publish/",
# not from another stage (no --from=build).
COPY publish/ .

USER ${APP_UID}
VOLUME ["/app/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "TravelEase.dll", "healthcheck", "/health/live"]

ENTRYPOINT ["dotnet", "TravelEase.dll"]
```

### Step 3 – Build and run it

```bash
docker build -f docker/Dockerfile.published -t travelease:published .
```

`-f` selects a Dockerfile other than the default. The final `.` is still the build context, and it must contain the `publish/` folder.

To use it with Compose, set this in `.env`:

```
DOCKERFILE=docker/Dockerfile.published
```

then run `docker compose up -d --build`.

> Repeat **Step 1** whenever the code changes, otherwise you package old files.

### Comparing the three approaches

| Approach | Who compiles? | Image size | Notes |
|---|---|---|---|
| **Multi-stage** (`Dockerfile`) | Docker, in stage 1 | ~333 MB | **Recommended.** One command, no SDK needed on your PC |
| **Final stage only** (`docker/Dockerfile.published`) | You or CI, *before* `docker build` | ~333 MB | Same size, but you must publish first, every time |
| **Single stage with SDK** (`docker/Dockerfile.single-stage`) | Docker, in the same stage | ~1.58 GB | Easy but bad: huge, runs as root, ships the source code and compiler |

> **Key point:** the **final images** of the first two approaches are practically the same. Multi-stage just moves the "publish first" step **inside** the Dockerfile, so the build can be repeated on any machine that has Docker.

---

## Part 8 – Running everything with plain `docker run` (no Compose)

This does by hand what Compose does, so you can see each piece on its own. (PowerShell syntax: a backtick `` ` `` continues a line.)

```powershell
# 1. A network, so the containers can find each other by name
docker network create travelease-net

# 2. The database container (official image) with a named volume
docker run -d --name db --network travelease-net `
  -e POSTGRES_DB=travelease -e POSTGRES_USER=travelease -e POSTGRES_PASSWORD=Secret123 `
  -v travelease-pgdata:/var/lib/postgresql/data `
  postgres:16-alpine

# 3. Build our image
docker build -t travelease:1.0.0 .

# 4. The app container: same network, DB_HOST = name of the db container
docker run -d --name app --network travelease-net -p 8080:8080 `
  -e DB_HOST=db -e DB_PASSWORD=Secret123 `
  -v travelease-data:/app/data `
  travelease:1.0.0

# 5. Check it
docker ps
docker logs -f app
# Browser: http://localhost:8080
```

In Linux/macOS bash, use `\` instead of `` ` ``, or write each command on one line.

Clean up:

```bash
docker rm -f app db
docker volume rm travelease-pgdata travelease-data
docker network rm travelease-net
```

---

## Part 9 – Command cheat sheet

### Build

| Command | Purpose |
|---|---|
| `docker build -t travelease:1.0.0 .` | build (multi-stage) |
| `docker build -t travelease:2.0.0 --build-arg APP_VERSION=2.0.0 .` | build with a build argument |
| `docker build -f docker/Dockerfile.published -t travelease:published .` | build the final-stage-only image |
| `docker images travelease` | list the images and their sizes |

### Compose

| Command | Purpose |
|---|---|
| `docker compose up -d --build` | build and start everything in the background |
| `docker compose ps` | status and health |
| `docker compose logs -f app` | follow the app logs |
| `docker compose exec db psql -U travelease` | open a SQL prompt in the database |
| `docker compose down` | stop and remove the containers (data kept) |
| `docker compose down -v` | …and delete the volumes (**all data lost**) |

### Inspect a running container

| Command | Purpose |
|---|---|
| `docker exec -it travelease-app-1 bash` | open a shell inside the app container |
| `docker exec travelease-app-1 id` | show which user runs the app (`uid=1654 app`) |
| `docker inspect --format "{{.State.Health.Status}}" travelease-app-1` | health status |
| `curl http://localhost:8080/health/ready` | call the health endpoint |
| `curl http://localhost:8080/api/info` | see which container answered |

### Try in the database (inside `psql`)

```sql
\dt                                                          -- list tables
SELECT COUNT(*) FROM "Bookings";
SELECT "BookingReference", "Status", "TotalPrice" FROM "Bookings" LIMIT 5;
\q                                                           -- quit
```

---

## Part 10 – Common questions

**Why not put the database inside the same container as the app?**
One container should have one responsibility. With separate containers, the app and the database can be restarted, upgraded, scaled and backed up independently, and PostgreSQL keeps its data in its own volume.

**Where is the data physically stored?**
In Docker volumes that Docker manages (see `docker volume ls` and `docker volume inspect pgdata`). Deleting the containers keeps the data; `docker compose down -v` deletes it.

**What does the app do if the database isn't ready yet?**
It still starts. `/health/live` returns **200** (the process is alive) and `/health/ready` returns **503** (not ready). The app retries the database every few seconds and logs `Database connection failed ... Retrying`. When the database comes up, the app migrates, seeds and becomes ready.

**Why isn't `EXPOSE` enough to open the app in the browser?**
`EXPOSE` only documents the port. `-p 8080:8080` (or `ports:` in Compose) actually publishes it on your computer.

**How do I run the app on another port?**
Change only the host side, `-p 9000:8080`, and browse to `http://localhost:9000`.
Or change the port inside the container too: `-e ASPNETCORE_HTTP_PORTS=9000 -p 9000:9000`.

**How do I switch to Development or Staging without rebuilding?**
Use `-e ASPNETCORE_ENVIRONMENT=Staging`. One image works for many environments.

**Why doesn't the app run as root?**
Least privilege. If the app is compromised, the attacker is an ordinary user who cannot modify the app files or install anything.

**Where do I see errors?**
Run `docker logs <container>`. Normal messages go to standard output and errors go to standard error. Production logs are JSON lines, which log tools can read easily.
