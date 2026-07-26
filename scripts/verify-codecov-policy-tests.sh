#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
verifier="$repository_root/scripts/verify-codecov-policy.sh"
base_sha="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
head_sha="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

equal_coverage='{"base_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":100,"hits":86,"misses":14,"partials":0},"patch":{"lines":0,"hits":0,"misses":0,"partials":0}}}'
increased_coverage='{"base_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":100,"hits":87,"misses":13,"partials":0},"patch":{"lines":2,"hits":2,"misses":0,"partials":0}}}'
decreased_coverage='{"base_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":100,"hits":85,"misses":15,"partials":0},"patch":{"lines":1,"hits":1,"misses":0,"partials":0}}}'
uncovered_patch='{"base_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":100,"hits":86,"misses":14,"partials":0},"patch":{"lines":1,"hits":0,"misses":1,"partials":0}}}'
empty_head='{"base_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":0,"hits":0,"misses":0,"partials":0},"patch":{"lines":0,"hits":0,"misses":0,"partials":0}}}'
wrong_comparison='{"base_commit":"cccccccccccccccccccccccccccccccccccccccc","head_commit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","totals":{"base":{"lines":100,"hits":86,"misses":14,"partials":0},"head":{"lines":100,"hits":86,"misses":14,"partials":0},"patch":{"lines":0,"hits":0,"misses":0,"partials":0}}}'

expect_success() {
  local name="$1"
  local payload="$2"

  if ! CODECOV_COMPARE_JSON="$payload" \
    GITHUB_REPOSITORY="georgepwall1991/ConfigContraband" \
    bash "$verifier" "$base_sha" "$head_sha" >/dev/null; then
    echo "Expected $name to pass." >&2
    exit 1
  fi
}

expect_failure() {
  local name="$1"
  local payload="$2"

  if CODECOV_COMPARE_JSON="$payload" \
    GITHUB_REPOSITORY="georgepwall1991/ConfigContraband" \
    bash "$verifier" "$base_sha" "$head_sha" >/dev/null 2>&1; then
    echo "Expected $name to fail." >&2
    exit 1
  fi
}

expect_success "equal project coverage with no coverable patch" "$equal_coverage"
expect_success "increased project coverage with a fully covered patch" "$increased_coverage"
expect_failure "decreased project coverage" "$decreased_coverage"
expect_failure "an uncovered patch line" "$uncovered_patch"
expect_failure "an empty head report after a non-empty base" "$empty_head"
expect_failure "a comparison for different commits" "$wrong_comparison"

echo "Codecov policy verifier tests passed."
