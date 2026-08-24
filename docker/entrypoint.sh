#!/bin/sh
set -e

# The image carries both the API and the EF Core migrations bundle, and this script decides which
# one the container is.
#
# Migrations are opt-in (LIBRARY_RUN_MIGRATIONS=true) rather than unconditional, because between 2
# and 11 replicas every one of them would otherwise run the migrator on start-up. EF takes an
# advisory lock, so that is safe rather than corrupting - but it also makes every replica wait on
# the first, and it makes a failed migration a crash loop across the whole deployment. Compose runs
# one dedicated migrator container; Kubernetes runs a Job. The API itself starts with this unset.
if [ "${LIBRARY_RUN_MIGRATIONS:-false}" = "true" ]; then
    if [ -z "$LIBRARY_DATA_CONNECTIONSTRING" ]; then
        echo "entrypoint: LIBRARY_DATA_CONNECTIONSTRING is unset; cannot apply migrations." >&2
        exit 1
    fi

    echo "entrypoint: applying EF Core migrations..."
    /app/library-migrate --connection "$LIBRARY_DATA_CONNECTIONSTRING"
    echo "entrypoint: migrations up to date."
fi

exec "$@"
