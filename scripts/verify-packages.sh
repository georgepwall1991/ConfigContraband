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

echo "Verified package versions, analyzer payloads, and README contents for $analyzer_version."
