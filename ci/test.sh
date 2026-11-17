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

# Check formatting. Fails when `dotnet format` (whitespace, code style and analyzers, at the severities .editorconfig
# sets) would change a file; `dotnet format src/Library.slnx` applies the same fixes. EF1002 is left out: it is an
# analyzer warning that no formatter can fix (LoanTransitionWriter interpolates a column name it picks itself into the
# SQL), and the build still reports it.
dotnet format "$SOLUTION" --verify-no-changes --no-restore --exclude-diagnostics EF1002

# Build solution. Built once here so the test run below runs with --no-build and does not pay for it.
dotnet build "$SOLUTION" --configuration Release --no-restore

# Run every test once: unit, integration and functional together. `dotnet test` runs the test projects side by side,
# each in a process of its own, so the unit projects finish while Library.Data.Tests and Library.WebApi.Tests start
# their PostgreSQL and Redis containers through Testcontainers. (The three suites used to run as three passes over the
# solution, one after another, each pass starting every test project's process only to find most of them had no test
# matching its filter.) The Category trait still separates them on your machine:
# dotnet test src/Library.slnx --filter "Category=Unit".
dotnet test "$SOLUTION" --configuration Release --no-build --collect:"XPlat Code Coverage"
