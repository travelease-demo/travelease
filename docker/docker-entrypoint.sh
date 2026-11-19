#!/bin/bash
# =============================================================================================
# TravelEase container entrypoint (used by docker/Dockerfile.instructions)
#
# ENTRYPOINT ["docker-entrypoint.sh"]  +  CMD ["dotnet", "TravelEase.dll"]
#   -> this script runs first, then `exec "$@"` replaces the shell with the CMD so the .NET
#      process becomes PID 1 and receives SIGTERM from `docker stop` directly.
#
# Optional environment variables:
#   WAIT_FOR_DB=true          wait until DB_HOST:DB_PORT accepts TCP connections before starting
#   WAIT_FOR_DB_TIMEOUT=60    seconds to wait
# =============================================================================================
set -euo pipefail

echo "------------------------------------------------------------"
echo " TravelEase container starting"
echo "   user        : $(id -un 2>/dev/null || echo unknown) (uid=$(id -u))"
echo "   environment : ${ASPNETCORE_ENVIRONMENT:-Production}"
echo "   http port(s): ${ASPNETCORE_HTTP_PORTS:-8080}"
echo "   app version : ${App__Version:-built-in}"
echo "   reports dir : ${Storage__ReportsPath:-/app/data/reports}"
echo "   command     : $*"
echo "------------------------------------------------------------"

# A freshly mounted empty volume may not contain the sub-directories yet.
mkdir -p "${Storage__ReportsPath:-/app/data/reports}" "${Storage__DataProtectionKeysPath:-/app/data/keys}" 2>/dev/null \
    || echo "WARN: cannot create data directories - check volume ownership for uid $(id -u)" >&2

if [[ "${WAIT_FOR_DB:-false}" == "true" && -n "${DB_HOST:-}" ]]; then
    port="${DB_PORT:-5432}"
    limit="${WAIT_FOR_DB_TIMEOUT:-60}"
    echo "Waiting up to ${limit}s for PostgreSQL at ${DB_HOST}:${port} ..."
    for ((i = 1; i <= limit; i++)); do
        if timeout 2 bash -c ">/dev/tcp/${DB_HOST}/${port}" 2>/dev/null; then
            echo "PostgreSQL is accepting connections (after ${i}s)."
            break
        fi
        if (( i == limit )); then
            echo "ERROR: PostgreSQL at ${DB_HOST}:${port} not reachable after ${limit}s" >&2
            exit 1
        fi
        sleep 1
    done
fi

# Replace this shell with the application (PID 1 => proper signal handling).
exec "$@"
