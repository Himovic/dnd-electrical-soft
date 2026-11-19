#!/usr/bin/env bash
# Builds the solution and runs all cross-platform test suites.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build DND-Electrical-Engineering-Suite.sln -c "${CONFIGURATION:-Release}"
for suite in UnitTests IntegrationTests UIAutomationTests; do
  dotnet test "tests/DND.Electrical.$suite" -c "${CONFIGURATION:-Release}" --no-build
done
