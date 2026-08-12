#!/usr/bin/env bash
set -euo pipefail

set +e
build_output="$(
  dotnet build samples/ConfigContraband.Quickstart/ConfigContraband.Quickstart.csproj \
    --configuration Release \
    --no-incremental \
    -tl:off \
    -p:ContinuousIntegrationBuild=true \
    -clp:NoSummary 2>&1
)"
build_status=$?
set -e

printf '%s\n' "$build_output"

actual="$(
  printf '%s\n' "$build_output" |
    sed -n 's/.* \(warning\|error\) \(CFG[0-9][0-9][0-9]\):.*/\2/p' |
    LC_ALL=C sort -u
)"

if [[ -n "$actual" ]]; then
  echo "Quickstart must emit zero CFG diagnostics." >&2
  echo "Actual:" >&2
  printf '%s\n' "$actual" >&2
  exit 1
fi

if [[ $build_status -ne 0 ]]; then
  echo "Quickstart build failed; expected a clean green-path Options registration." >&2
  exit "$build_status"
fi

echo "Quickstart contract passed: zero CFG001-CFG010 diagnostics."
