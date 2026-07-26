#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
consumer_dir="$repository_root/tests/Compatibility/Net8Consumer"
consumer_project="$consumer_dir/Net8Consumer.csproj"
package_dir="${1:-$repository_root/artifacts/packages}"
package_dir="$(cd "$package_dir" && pwd -P)"
analyzer_version="$(
  dotnet msbuild "$repository_root/src/ConfigContraband/ConfigContraband.csproj" \
    -getProperty:Version \
    -nologo
)"
candidate_package="$package_dir/ConfigContraband.$analyzer_version.nupkg"

test -f "$candidate_package"

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/configcontraband-net8-host.XXXXXXXX")"
trap 'rm -rf "$work_dir"' EXIT

packages_dir="$work_dir/nuget-packages"

build_properties=(
  "-p:ConfigContrabandVersion=$analyzer_version"
  "-p:RestoreSources=$package_dir;https://api.nuget.org/v3/index.json"
  "-p:BaseIntermediateOutputPath=$work_dir/obj/"
  "-p:BaseOutputPath=$work_dir/bin/"
)

cd "$consumer_dir"

NUGET_PACKAGES="$packages_dir" dotnet restore \
  "$consumer_project" \
  --no-cache \
  "${build_properties[@]}"

package_metadata="$packages_dir/configcontraband/$analyzer_version/.nupkg.metadata"
test -f "$package_metadata"

python3 - "$package_metadata" "$package_dir" <<'PY'
import json
import pathlib
import sys
import urllib.parse

metadata_path = pathlib.Path(sys.argv[1])
expected_source = pathlib.Path(sys.argv[2]).resolve()
metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
source = metadata.get("source")

if not source:
    raise SystemExit(f"{metadata_path} does not record a package source")

parsed_source = urllib.parse.urlparse(source)
if parsed_source.scheme == "file":
    actual_source = pathlib.Path(urllib.parse.unquote(parsed_source.path)).resolve()
elif parsed_source.scheme:
    raise SystemExit(f"expected a local package source, but NuGet restored from {source}")
else:
    actual_source = pathlib.Path(source).resolve()

if actual_source != expected_source:
    raise SystemExit(
        f"expected candidate source {expected_source}, but NuGet restored from {actual_source}"
    )
PY

set +e
build_output="$(
  NUGET_PACKAGES="$packages_dir" dotnet build \
    "$consumer_project" \
    --configuration Release \
    --no-restore \
    -p:ContinuousIntegrationBuild=true \
    "${build_properties[@]}" \
    2>&1
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

echo "Verified packed ConfigContraband $analyzer_version execution under the .NET 8 compiler host."
