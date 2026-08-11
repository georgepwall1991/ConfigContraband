#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"
analyzer_version="$(dotnet msbuild src/ConfigContraband/ConfigContraband.csproj -getProperty:Version -nologo)"
tool_version="$(dotnet msbuild src/ConfigContraband.Tool/ConfigContraband.Tool.csproj -getProperty:Version -nologo)"

if [[ "$analyzer_version" != "$tool_version" ]]; then
  echo "Analyzer and tool package versions must match: analyzer=$analyzer_version tool=$tool_version" >&2
  exit 1
fi

analyzer_package="$package_dir/ConfigContraband.$analyzer_version.nupkg"
tool_package="$package_dir/ConfigContraband.Tool.$tool_version.nupkg"

test -f "$analyzer_package"
test -f "$tool_package"

cmp src/ConfigContraband/bin/Release/netstandard2.0/ConfigContraband.dll \
  <(unzip -p "$analyzer_package" analyzers/dotnet/cs/ConfigContraband.dll)
cmp src/ConfigContraband.Core/bin/Release/netstandard2.0/ConfigContraband.Core.dll \
  <(unzip -p "$analyzer_package" analyzers/dotnet/cs/ConfigContraband.Core.dll)
cmp README.md <(unzip -p "$analyzer_package" README.md)
cmp README.md <(unzip -p "$tool_package" README.md)

# Product-flow visuals referenced by PackageReadmeFile must ship inside both packages.
for asset in \
  assets/configcontraband-icon.png \
  assets/flow-ide-diagnostics.svg \
  assets/flow-before-after-fix.svg \
  assets/flow-analyzer-schema-loop.svg
do
  cmp "$asset" <(unzip -p "$analyzer_package" "$asset")
  cmp "$asset" <(unzip -p "$tool_package" "$asset")
done

# Discoverability metadata: high-intent Options / appsettings terms (NuGet search).
analyzer_nuspec="$(unzip -p "$analyzer_package" ConfigContraband.nuspec)"
tool_nuspec="$(unzip -p "$tool_package" ConfigContraband.Tool.nuspec)"

for term in ValidateOnStart ValidateDataAnnotations BindConfiguration IOptions appsettings validation; do
  printf '%s' "$analyzer_nuspec" | grep -Fq "$term" || {
    echo "Analyzer nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

for term in appsettings.schema.json ValidateDataAnnotations BindConfiguration json-schema; do
  printf '%s' "$tool_nuspec" | grep -Fq "$term" || {
    echo "Tool nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

echo "Verified package versions, analyzer payloads, README, assets, and discoverability metadata for $analyzer_version."
