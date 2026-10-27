#!/usr/bin/env bash
# The `test` check: the steps of tests.yml's `test` job, in the same order. yggdrasil.yml runs it in the pinned .NET
# SDK image from the repository's root, with the Docker engine (the integration and functional tests start PostgreSQL
# and Redis with Testcontainers).
#
# The workflow wrote the vulnerability report to vulnerabilities.txt in the checkout; here it goes to a temporary file
# that is removed afterwards, so the check leaves nothing in the tree. The test results the workflow uploaded
# ('**/TestResults/**') are what yggdrasil.yml archives.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

SOLUTION=src/Library.slnx

# Restore dependencies
dotnet restore "$SOLUTION"

# Scan for vulnerable dependencies. Straight after restore and before the build: it needs the resolved graph and
# nothing else, so a published advisory is reported in seconds rather than after the container suites.
#
# A dependency does not have to change to become vulnerable - the advisory arrives on its own schedule - which is
# why this runs on every build rather than only when a package file changes.
report=$(mktemp)
trap 'rm -f "$report"' EXIT
dotnet list "$SOLUTION" package --vulnerable --include-transitive > "$report"
cat "$report"
if grep -q "has the following vulnerable packages" "$report"; then
  echo "ci/test.sh: Vulnerable packages found. See the report above." >&2
  exit 1
fi

# Build solution. Built once here so all three test steps run with --no-build and none of them pays for it.
dotnet build "$SOLUTION" --configuration Release --no-restore

# Run unit tests. The Category trait comes from the [UnitFact] / [UnitTheory] attributes, so this filter needs no
# per-project configuration.
dotnet test "$SOLUTION" --configuration Release --no-build --filter "Category=Unit" --collect:"XPlat Code Coverage"

# Run integration tests. These start real PostgreSQL and Redis containers through Testcontainers.
dotnet test "$SOLUTION" --configuration Release --no-build --filter "Category=Integration" --collect:"XPlat Code Coverage"

# Run functional tests
dotnet test "$SOLUTION" --configuration Release --no-build --filter "Category=Functional" --collect:"XPlat Code Coverage"
