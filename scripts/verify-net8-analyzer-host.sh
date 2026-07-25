#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
consumer_dir="$repository_root/tests/Compatibility/Net8Consumer"

set +e
build_output="$(
  cd "$consumer_dir"
  dotnet build --configuration Release -p:ContinuousIntegrationBuild=true 2>&1
)"
build_status=$?
set -e

printf '%s\n' "$build_output"

if [[ $build_status -eq 0 ]]; then
  echo "Expected the compatibility smoke build to fail on CFG001, but it succeeded." >&2
  exit 1
fi

if [[ "$build_output" != *"error CFG001"* ]]; then
  echo "The .NET 8 compiler host did not execute ConfigContraband and emit CFG001." >&2
  exit 1
fi

if [[ "$build_output" == *"CS9057"* ]]; then
  echo "The .NET 8 compiler host rejected ConfigContraband with CS9057." >&2
  exit 1
fi

echo "Verified ConfigContraband execution under the .NET 8 compiler host."
