#!/usr/bin/env bash
# Tests of the command parsing of deploy.sh, the only input the deploy key can pass to the server.
# Run: bash deploy/deploy.test.sh
set -uo pipefail

# shellcheck source=deploy/deploy.sh
source "$(dirname "${BASH_SOURCE[0]}")/deploy.sh"

failures=0

accepts() {
  local command="$1" expected="$2" actual
  if actual="$(parse_command "$command" 2>/dev/null)" && [[ "$actual" == "$expected" ]]; then
    echo "ok: accepts '$command' as $expected"
  else
    echo "FAIL: '$command' should give $expected, got '${actual:-}'"
    failures=$((failures + 1))
  fi
}

rejects() {
  local command="$1" actual
  if actual="$(parse_command "$command" 2>/dev/null)"; then
    echo "FAIL: '$command' should be rejected, got '$actual'"
    failures=$((failures + 1))
  else
    echo "ok: rejects '$command'"
  fi
}

protocol="$DEPLOY_PROTOCOL"

accepts "deploy 1 $protocol" 1
accepts "deploy 7 $protocol" 7
accepts "deploy 120 $protocol" 120

rejects ""
rejects "deploy"
rejects "deploy 7"
rejects "rollback 7 $protocol"
rejects "Deploy 7 $protocol"
rejects "deploy 0 $protocol"
rejects "deploy 07 $protocol"
rejects "deploy -1 $protocol"
rejects "deploy +1 $protocol"
rejects "deploy abc $protocol"
rejects "deploy 1.5 $protocol"
rejects "deploy 7 $protocol extra"
rejects "deploy 7 $protocol; rm -rf ~"
rejects "deploy 7;id $protocol"
rejects "deploy \$(id) $protocol"
rejects "deploy 7 $protocol|id"
rejects "deploy 7 $protocol&"
rejects "deploy 7 $protocol"$'\n'"id"
rejects " deploy 7 $protocol"
rejects "deploy  7 $protocol"
rejects "deploy"$'\t'"7 $protocol"
rejects "deploy 7 $((protocol + 1))"
rejects "deploy 7 0$protocol"
rejects "deploy 7 x"

# Another protocol means deploy.sh on the server and deploy.yml come from different commits: the error says what to do.
error="$(parse_command "deploy 7 $((protocol + 1))" 2>&1 >/dev/null)"
if [[ "$error" == *"copy deploy/deploy.sh"* ]]; then
  echo "ok: another protocol asks to copy deploy.sh to the server"
else
  echo "FAIL: another protocol should ask to copy deploy.sh to the server"
  failures=$((failures + 1))
fi

# The commit is in main when main is the commit itself or comes after it. GitHub answers for main as the base.
compare_status=""
github() {
  [[ "$1" == "compare/main...$commit" ]] && echo "{\"status\": \"$compare_status\"}"
}
commit=0123456789abcdef0123456789abcdef01234567

in_main_gives() {
  compare_status="$1"
  if in_main "$commit"; then actual=yes; else actual=no; fi
  if [[ "$actual" == "$2" ]]; then
    echo "ok: compare status '$1' gives in main: $2"
  else
    echo "FAIL: compare status '$1' should give in main: $2, got $actual"
    failures=$((failures + 1))
  fi
}

in_main_gives behind yes
in_main_gives identical yes
in_main_gives ahead no
in_main_gives diverged no

if ((failures > 0)); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
