#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
package_dir="${1:-$repo_root/artifacts/packages}"
package_dir="$(cd "$package_dir" && pwd)"
tool_version="$(dotnet msbuild "$repo_root/src/ConfigContraband.Tool/ConfigContraband.Tool.csproj" -getProperty:Version -nologo)"
tool_package="$package_dir/ConfigContraband.Tool.$tool_version.nupkg"

test -f "$tool_package"

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/configcontraband-packed-tool.XXXXXXXX")"
trap 'rm -rf "$work_dir"' EXIT

tool_dir="$work_dir/tool"
stdout_file="$work_dir/stdout"
stderr_file="$work_dir/stderr"
generated_schema="$work_dir/appsettings.schema.json"
showcase_project="$repo_root/samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj"
expected_schema="$repo_root/samples/ConfigContraband.Showcase/appsettings.schema.json"

NUGET_PACKAGES="$work_dir/nuget-packages" dotnet tool install ConfigContraband.Tool \
  --version "$tool_version" \
  --tool-path "$tool_dir" \
  --source "$package_dir" \
  --no-http-cache

tool="$tool_dir/configcontraband"

help_output="$("$tool" --help)"
case "$help_output" in
  *"ConfigContraband schema generator"*"configcontraband schema [options]"*) ;;
  *)
    echo "Packed tool help output did not contain the expected usage." >&2
    exit 1
    ;;
esac

dotnet restore "$showcase_project"
"$tool" schema --project "$showcase_project" --output "$generated_schema"
cmp "$expected_schema" "$generated_schema"
"$tool" schema --project "$showcase_project" --output "$generated_schema" --check

printf '{}\n' > "$generated_schema"

expect_exit()
{
  local expected="$1"
  local actual
  shift

  set +e
  "$@" >"$stdout_file" 2>"$stderr_file"
  actual=$?
  set -e

  if [[ "$actual" -ne "$expected" ]]; then
    echo "Expected exit $expected, got $actual: $*" >&2
    cat "$stdout_file" >&2
    cat "$stderr_file" >&2
    exit 1
  fi
}

expect_exit 1 "$tool" schema --project "$showcase_project" --output "$generated_schema" --check
case "$(cat "$stderr_file")" in
  *"Schema is out of date."*) ;;
  *)
    echo "Packed tool stale-schema check did not report the expected error." >&2
    cat "$stderr_file" >&2
    exit 1
    ;;
esac

expect_exit 2 "$tool" schema --project "$work_dir/Missing.csproj" --check
case "$(cat "$stderr_file")" in
  *"error: failed to load project:"*) ;;
  *)
    echo "Packed tool load failure did not report the expected error." >&2
    cat "$stderr_file" >&2
    exit 1
    ;;
esac

echo "Verified packed ConfigContraband.Tool $tool_version help, generation, current, stale, and load-failure paths."
