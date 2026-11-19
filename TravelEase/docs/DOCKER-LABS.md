# TravelEase Docker Labs

These hands-on exercises use the TravelEase application to practise Dockerfile instructions and container workflows. Run every command from the `TravelEase/` folder. PowerShell users: replace `\` line continuations with a backtick `` ` ``, or put the command on one line.

**Setup:** create a network and a PostgreSQL container that the labs share.

```bash
docker network create travelease-net
docker run -d --name pg --network travelease-net \
  -e POSTGRES_DB=travelease -e POSTGRES_USER=travelease -e POSTGRES_PASSWORD=labpass \
  -v travelease-labdb:/var/lib/postgresql/data postgres:16-alpine
```

Containers on `travelease-net` reach the database at host name **`pg`**.

---

## Instruction map

| Instruction | Where to see it | Lab |
|---|---|---|
| `FROM` | All Dockerfiles: `sdk` for build, `aspnet` for runtime, `scratch` for export | 1, 2 |
| `WORKDIR` | `/src` (build), `/app` (runtime) | 1 |
| `COPY` | csproj first, then the source; `--from=build` between stages | 1, 3 |
| `ADD` | `docker/Dockerfile.instructions`: auto-extracts `sample-reports.tar.gz` | 9 |
| `RUN` | restore, publish, apt-get, useradd, chown | 1, 4 |
| `CMD` | `Dockerfile.instructions` (exec form), `Dockerfile.single-stage` (shell form) | 6 |
| `ENTRYPOINT` | `Dockerfile` (exec form), `Dockerfile.instructions` (script + `exec "$@"`) | 6 |
| `EXPOSE` | `8080` | 5 |
| `ENV` | `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `Storage__*` | 5, 7 |
| `ARG` | `DOTNET_VERSION` (before `FROM`), `BUILD_CONFIGURATION`, `APP_VERSION`, `BUILD_DATE` | 4 |
| `USER` | `$APP_UID` (built-in `app` user, 1654), custom `travelease` (10001) | 8 |
| `LABEL` | OCI `org.opencontainers.image.*` labels | 4 |
| `SHELL` | `["/bin/bash", "-o", "pipefail", "-c"]` | 9 |
| `VOLUME` | `/app/data` (reports and keys) | 9 |
| `HEALTHCHECK` | Built-in probe (`dotnet TravelEase.dll healthcheck`) or `curl` | 10 |

---

## Lab 1: Build a multi-stage image from source

```bash
docker build -t travelease:1.0.0 .
docker image history travelease:1.0.0
```

* Edit a `.cshtml` file and rebuild. The `dotnet restore` step shows `CACHED`, because `COPY TravelEase.csproj` comes before `COPY . .`.
* Look at the `transferring context` size, then read `.dockerignore`. What would happen without it, with `bin/` and `obj/` from the host copied in?

## Lab 2: Build from published files (no SDK in the image)

```bash
# Export the publish output with Docker (no local SDK needed) ...
docker build --target export --output publish .
# ... or with a local SDK:  dotnet publish -c Release -o publish -p:UseAppHost=false
docker build -f docker/Dockerfile.published -t travelease:published .
```

How does `FROM scratch AS export` together with `--output` write files to your disk?

## Lab 3: Run the container

```bash
docker run -d --name te --network travelease-net -p 8080:8080 \
  -e DB_HOST=pg -e DB_PASSWORD=labpass \
  travelease:1.0.0

docker logs -f te           # migrations, seeding, "TravelEase started"
curl localhost:8080/api/info
```

Open http://localhost:8080 and look at **Settings**: the host name is the container ID, the user is `app`, and the environment is Production.

## Lab 4: Build arguments, labels and versioning

```bash
docker build -t travelease:2.0.0 \
  --build-arg APP_VERSION=2.0.0 \
  --build-arg BUILD_DATE=$(date -u +%Y-%m-%dT%H:%M:%SZ) \
  --build-arg VCS_REF=abc1234 .

docker inspect --format '{{json .Config.Labels}}' travelease:2.0.0
docker inspect --format '{{json .Config.Env}}' travelease:2.0.0     # App__Version=2.0.0 came from the ARG
# Run it (see Lab 3) and check the version in the sidebar, on /settings and on /api/info
```

* `APP_VERSION` is compiled into the assembly (`-p:Version`) **and** copied into `ENV App__Version`.
* Try `--build-arg BUILD_CONFIGURATION=Debug` and compare the image sizes.
* `ARG DOTNET_VERSION` comes before the first `FROM`, so `--build-arg DOTNET_VERSION=8.0` changes the base image tags.

## Lab 5: Ports and environment variables

```bash
# Change the internal port. EXPOSE is only documentation; -p does the publishing.
docker run -d --name te9090 --network travelease-net -p 9090:9090 \
  -e ASPNETCORE_HTTP_PORTS=9090 -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:1.0.0
curl localhost:9090/health/live

# Change the environment and other configuration with no rebuild
docker run -d --name te-staging --network travelease-net -p 8081:8080 \
  -e ASPNETCORE_ENVIRONMENT=Staging \
  -e App__Name="TravelEase Staging" -e App__CurrencySymbol='$' \
  -e Logging__Console__FormatterName=simple \
  -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:1.0.0
```

The staging container shows a yellow **STAGING** badge, the new name and currency, and human-readable logs.

* Pass a full connection string instead: `-e ConnectionStrings__DefaultConnection="Host=pg;Database=travelease;Username=travelease;Password=labpass"`.
* Use an env file: `docker run --env-file .env ...`.
* Try a Docker secret-style file: mount a file and set `-e DB_PASSWORD_FILE=/run/secrets/db_password`.

## Lab 6: CMD vs ENTRYPOINT

```bash
docker build -f docker/Dockerfile.instructions -t travelease:instructions .

# 1. Default: ENTRYPOINT script + CMD ["dotnet","TravelEase.dll"]
docker run --rm --network travelease-net -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:instructions
#    The banner comes from docker-entrypoint.sh, then exec "$@" starts .NET as PID 1.

# 2. Replace CMD: arguments after the image name replace CMD and are passed to the ENTRYPOINT
docker run --rm travelease:instructions dotnet TravelEase.dll healthcheck   # probe once, exit 1 (no app running)
docker run --rm travelease:instructions id                                   # the banner, then "id"

# 3. Replace ENTRYPOINT
docker run --rm -it --entrypoint bash travelease:instructions

# 4. Production Dockerfile: ENTRYPOINT ["dotnet","TravelEase.dll"] with no CMD.
#    Arguments are APPENDED to the entrypoint:
docker run --rm -p 7070:7070 travelease:1.0.0 --urls http://+:7070
```

**Signal handling (shell form vs exec form):**

```bash
docker build -f docker/Dockerfile.single-stage -t travelease:single-stage .
docker run -d --name shellform --network travelease-net -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:single-stage
docker run -d --name execform  --network travelease-net -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:1.0.0
docker exec shellform cat /proc/1/cmdline | tr '\0' ' '   # /bin/sh -c dotnet TravelEase.dll
docker exec execform  cat /proc/1/cmdline | tr '\0' ' '   # dotnet TravelEase.dll
time docker stop execform
time docker stop shellform
docker logs execform  | tail -3     # "...is shutting down" and "...stopped cleanly"
docker logs shellform | tail -3     # compare
```

With exec form, `dotnet` is PID 1 and receives SIGTERM directly. With shell form, PID 1 is `/bin/sh`, so your application depends on the shell to pass the signal on. Compare the stop times and the final log lines. This is why production Dockerfiles use the exec form (JSON array), and why an entrypoint script ends with `exec "$@"`.

## Lab 7: Multiple environments from the same image

Run the same `travelease:1.0.0` image three times with `ASPNETCORE_ENVIRONMENT` set to `Development`, `Staging` and `Production`. Compare:

* the sidebar environment badge
* the log format (`simple` in Development, `json` elsewhere)
* the error page (developer exception page only in Development)

Note that `appsettings.Development.json` sets `Host=localhost`. `DB_HOST=pg` still wins, because environment variables override JSON settings.

## Lab 8: Non-root user

```bash
docker run --rm --entrypoint id travelease:1.0.0              # uid=1654(app)  - built-in user of the .NET images
docker run --rm --entrypoint id travelease:instructions       # uid=10001(travelease) - created with useradd
docker run --rm --entrypoint id travelease:single-stage       # uid=0(root)    - no USER instruction
docker run --rm --entrypoint id --user 0 travelease:1.0.0      # run as root (avoid this)
docker run --rm --entrypoint sh travelease:1.0.0 -c "touch /app/test"   # Permission denied: app files are root-owned and read-only
```

The **Settings** page shows the process user, with a warning when it is root.

## Lab 9: Volumes, ADD and SHELL

```bash
# Named volume: reports survive container removal
docker run -d --name te-vol --network travelease-net -p 8080:8080 \
  -e DB_HOST=pg -e DB_PASSWORD=labpass -v travelease-reports:/app/data travelease:instructions
```

Open **Reports → Export CSV**, then the **Generated files** tab. The `README.txt` and `sample-destinations.csv` files were extracted by `ADD` and copied into the new volume.

```bash
docker rm -f te-vol
docker run --rm -v travelease-reports:/data alpine ls -l /data/reports    # the files are still there
docker volume inspect travelease-reports

# Bind mount (host folder). On Linux, make it writable for UID 10001 first.
mkdir -p ./data-host && docker run -d --name te-bind ... -v "$(pwd)/data-host:/app/data" travelease:instructions

# Anonymous volume: VOLUME creates one automatically when you pass no -v
docker run -d --name te-anon --network travelease-net -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:1.0.0
docker inspect --format '{{json .Mounts}}' te-anon
```

**SHELL:** in `Dockerfile.instructions`, `RUN dotnet restore ... | tee /tmp/restore.log` relies on `-o pipefail`. Temporarily break the csproj (for example, add an invalid package version) and build with and without the `SHELL` line. Without `pipefail`, the build passes even though the restore failed.

## Lab 10: HEALTHCHECK

```bash
docker run -d --name te-h --network travelease-net -e DB_HOST=pg -e DB_PASSWORD=labpass travelease:1.0.0
watch docker ps                                             # starting -> healthy
docker inspect --format '{{json .State.Health}}' te-h

docker stop pg                                              # break the database
# about 90 s later te-h becomes "unhealthy": /health/ready returns 503, while /health/live stays 200
docker start pg                                             # recovers to "healthy"
```

Compare the two styles:

* `Dockerfile`: `CMD ["dotnet","TravelEase.dll","healthcheck","/health/ready"]`. No curl is needed, which suits minimal and chiseled images.
* `Dockerfile.instructions`: `CMD curl -fsS http://localhost:${ASPNETCORE_HTTP_PORTS}/health/ready`. This is shell form, so the variable expands when the check runs.

Override at run time with `--health-cmd`, `--health-interval` or `--no-healthcheck`.

## Lab 11: Image size optimisation

```bash
docker build -f docker/Dockerfile.single-stage -t travelease:single-stage .
docker build -f docker/Dockerfile.alpine       -t travelease:alpine .
docker images travelease
```

Sizes measured when this project was built (Docker Desktop, `docker images`):

| Image | Base | Size |
|---|---|---|
| `single-stage` | SDK (compilers, NuGet cache, source) | 1.58 GB |
| `instructions` | aspnet (Debian) + curl + entrypoint | 341 MB |
| `1.0.0` / `published` | aspnet (Debian) | 333 MB |
| `alpine` | aspnet-alpine + ICU | 224 MB |

Further ideas:

* `mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled`: no shell or package manager. The built-in health probe works there, but curl and the entrypoint script do not.
* Trimming and self-contained publishing (`-p:PublishTrimmed=true`) are not recommended for MVC apps that use reflection.

## Lab 12: Logs

```bash
docker logs te                         # stdout + stderr
docker logs te 2>/dev/null             # stdout only (info and warnings)
docker logs te 2>&1 >/dev/null         # stderr only (errors)
docker logs --since 5m -f te
docker logs te | grep -i booking       # booking created / confirmed / cancelled events
```

Stop the database to generate `Database connection failed` and health check errors, then read them on stderr.

## Lab 13: Docker Compose

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
docker compose exec db psql -U travelease -c 'select "BookingReference","Status","TotalPrice" from "Bookings" order by "BookingDate" desc limit 5;'
DOCKERFILE=docker/Dockerfile.instructions docker compose up -d --build app   # swap the Dockerfile
APP_PORT=9000 docker compose up -d                                         # change the published port
docker compose down -v
```

Things to notice: `depends_on: condition: service_healthy` waits for `pg_isready`, the service name `db` is the DNS name, and named volumes persist data.

## Cleanup

```bash
docker rm -f te te9090 te-staging te-h te-anon te-bind shellform execform pg 2>/dev/null
docker volume rm travelease-labdb travelease-reports 2>/dev/null
docker network rm travelease-net
docker image rm travelease:single-stage travelease:alpine travelease:instructions travelease:published
```
