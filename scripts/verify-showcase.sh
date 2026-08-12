#!/usr/bin/env bash
set -euo pipefail

set +e
build_output="$(
  dotnet build samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj \
    --configuration Release \
    --no-incremental \
    -tl:off \
    -p:ContinuousIntegrationBuild=true \
    -clp:NoSummary 2>&1
)"
build_status=$?
set -e

printf '%s\n' "$build_output"

if [[ $build_status -ne 0 ]]; then
  echo "Showcase build failed before its diagnostic contract could be checked." >&2
  exit "$build_status"
fi

actual="$(
  printf '%s\n' "$build_output" |
    sed '/^Build succeeded\.$/q' |
    sed -n 's/.* warning \(CFG[0-9][0-9][0-9]\):.*/\1/p' |
    LC_ALL=C sort
)"
expected="$(
  printf '%s\n' \
    CFG001 \
    CFG002 \
    CFG003 \
    CFG004 \
    CFG005 \
    CFG006 \
    CFG007 \
    CFG008 \
    CFG009 \
    CFG010
)"

if [[ "$actual" != "$expected" ]]; then
  echo "Showcase diagnostics did not match the exact CFG001-CFG010 contract." >&2
  echo "Expected:" >&2
  printf '%s\n' "$expected" >&2
  echo "Actual:" >&2
  printf '%s\n' "${actual:-<none>}" >&2
  exit 1
fi

echo "Showcase diagnostic contract passed: exactly one each of CFG001-CFG010."
