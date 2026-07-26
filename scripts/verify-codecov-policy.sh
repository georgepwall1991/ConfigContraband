#!/usr/bin/env bash
set -euo pipefail

base_sha="${1:?usage: verify-codecov-policy.sh <base-sha> <head-sha>}"
head_sha="${2:?usage: verify-codecov-policy.sh <base-sha> <head-sha>}"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must identify the owner and repository}"

if [[ ! "$base_sha" =~ ^[0-9a-fA-F]{40}$ ]] || [[ ! "$head_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "Codecov policy verification requires full 40-character commit SHAs." >&2
  exit 1
fi

IFS=/ read -r repository_owner repository_name repository_extra <<< "$repository"
if [[ -z "$repository_owner" || -z "$repository_name" || -n "${repository_extra:-}" ]] ||
  [[ ! "$repository_owner" =~ ^[A-Za-z0-9_.-]+$ ]] ||
  [[ ! "$repository_name" =~ ^[A-Za-z0-9_.-]+$ ]]; then
  echo "GITHUB_REPOSITORY must be an owner/repository slug." >&2
  exit 1
fi

comparison_file="$(mktemp "${TMPDIR:-/tmp}/configcontraband-codecov.XXXXXXXX")"
trap 'rm -f "$comparison_file"' EXIT

comparison_is_ready() {
  python3 - "$comparison_file" "$base_sha" "$head_sha" <<'PY'
import json
import pathlib
import sys

payload = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
base_sha = sys.argv[2].lower()
head_sha = sys.argv[3].lower()
totals = payload.get("totals")

if payload.get("base_commit", "").lower() != base_sha:
    raise SystemExit(1)
if payload.get("head_commit", "").lower() != head_sha:
    raise SystemExit(1)
if not isinstance(totals, dict):
    raise SystemExit(1)
if not all(isinstance(totals.get(name), dict) for name in ("base", "head", "patch")):
    raise SystemExit(1)
PY
}

if [[ -n "${CODECOV_COMPARE_JSON:-}" ]]; then
  printf '%s' "$CODECOV_COMPARE_JSON" > "$comparison_file"
  if ! comparison_is_ready; then
    echo "The supplied Codecov comparison does not match the requested commits." >&2
    exit 1
  fi
else
  poll_attempts="${CODECOV_POLL_ATTEMPTS:-30}"
  poll_seconds="${CODECOV_POLL_SECONDS:-2}"
  if [[ ! "$poll_attempts" =~ ^[1-9][0-9]*$ ]] || [[ ! "$poll_seconds" =~ ^[0-9]+$ ]]; then
    echo "Codecov polling settings must be non-negative integers with at least one attempt." >&2
    exit 1
  fi

  compare_url="https://api.codecov.io/api/v2/github/$repository_owner/repos/$repository_name/compare/?base=$base_sha&head=$head_sha"
  comparison_ready=false

  for ((attempt = 1; attempt <= poll_attempts; attempt++)); do
    if curl --fail --silent --show-error "$compare_url" --output "$comparison_file" &&
      comparison_is_ready; then
      comparison_ready=true
      break
    fi

    if ((attempt < poll_attempts)); then
      sleep "$poll_seconds"
    fi
  done

  if [[ "$comparison_ready" != true ]]; then
    echo "Codecov did not provide the requested comparison after $poll_attempts attempts." >&2
    exit 1
  fi
fi

python3 - "$comparison_file" <<'PY'
from decimal import Decimal
import json
import pathlib
import sys

payload = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
totals = payload["totals"]


def read_totals(name):
    result = totals[name]
    values = {}
    for field in ("lines", "hits", "misses", "partials"):
        value = result.get(field)
        if not isinstance(value, int) or isinstance(value, bool) or value < 0:
            raise SystemExit(f"Codecov {name} {field} must be a non-negative integer.")
        values[field] = value

    if values["lines"] != values["hits"] + values["misses"] + values["partials"]:
        raise SystemExit(f"Codecov {name} totals are internally inconsistent.")
    return values


def percentage(values):
    if values["lines"] == 0:
        return Decimal(0)
    return Decimal(values["hits"]) * Decimal(100) / Decimal(values["lines"])


base = read_totals("base")
head = read_totals("head")
patch = read_totals("patch")

if base["lines"] > 0 and head["lines"] == 0:
    raise SystemExit(
        "Codecov returned an empty head report after a non-empty base report."
    )

if head["hits"] * base["lines"] < base["hits"] * head["lines"]:
    raise SystemExit(
        "Project coverage decreased "
        f"from {percentage(base):.2f}% to {percentage(head):.2f}%."
    )

if patch["lines"] > 0 and (
    patch["hits"] != patch["lines"] or patch["misses"] != 0 or patch["partials"] != 0
):
    raise SystemExit(
        "Patch coverage is below 100% "
        f"({patch['hits']} fully covered of {patch['lines']} coverable lines)."
    )

print(
    "Codecov policy passed: "
    f"project {percentage(base):.2f}% -> {percentage(head):.2f}%; "
    + (
        "no changed coverable lines."
        if patch["lines"] == 0
        else f"patch {patch['hits']}/{patch['lines']} fully covered."
    )
)
PY
