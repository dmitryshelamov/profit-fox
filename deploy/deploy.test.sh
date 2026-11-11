#!/usr/bin/env bash
# Tests of the command parsing of deploy.sh, the only input the deploy key can pass to the server, and of the order of its roll-out.
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

# The roll-out of a version, with compose, docker and the wait for the API replaced: each writes what it was asked to a log, from a subshell
# too, as roll_out reads the services and the containers in subshells and fails by exit.
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
services="api worker scanner web db trade-flow"
api_answers=yes
compose() {
  echo "compose $*" >>"$log"
  if [[ "$*" == "config --services" ]]; then
    tr ' ' '\n' <<<"$services"
  fi
}
docker() {
  echo "docker $*" >>"$log"
  if [[ "$1" == ps ]]; then
    echo collector-1
  fi
}
wait_for_api() {
  echo "wait for api" >>"$log"
  [[ "$api_answers" == yes ]]
}
version=8

# Runs roll_out in a subshell; leaves its output, its status and the calls it made.
roll() {
  : >"$log"
  output="$( (roll_out) 2>&1)"
  status=$?
  made="$(tr '\n' ';' <"$log")"
}

# Takes the status of the check just made.
check() {
  local held=$? name="$1"
  if ((held == 0)); then
    echo "ok: $name"
  else
    echo "FAIL: $name"$'\n'"  status $status, output '$output'"$'\n'"  calls $made"
    failures=$((failures + 1))
  fi
}

stopping="compose config --services;compose stop worker scanner;docker ps --quiet --filter label=com.docker.compose.service=trade-flow;docker stop collector-1;"
api="compose up --detach --wait db;compose up --detach --no-deps api;wait for api;"

# Scenario "Обычная выкатка": the collector stops with the Worker and the scanner and starts after the API answered on the new version.
roll
((status == 0)) && [[ "$made" == "${stopping}${api}compose up --detach --no-deps worker scanner web;compose up --detach --no-deps trade-flow;" ]]
check "a deploy stops the collector with the Worker and the scanner and starts it after the API"

# Scenario "API не поднялся": the deploy fails, and the Worker, the scanner and the collector stay stopped.
api_answers=no
roll
((status != 0)) && [[ "$made" == "${stopping}${api}" && "$output" == *"the collector stay stopped"* ]]
check "an API that does not answer fails the deploy and starts neither the Worker, nor the scanner, nor the collector"
api_answers=yes

# Scenario "Откат на версию без сборщика": the version has no such service, so the collector of the later version stops and stays stopped.
services="api worker scanner web db"
roll
((status == 0)) && [[ "$made" == "${stopping}${api}compose up --detach --no-deps worker scanner web;" &&
  "$output" == *"has no collector of the trade flow; it stays stopped"* ]]
check "a version without the collector stops it and leaves it stopped"

# The images whose revision is checked: the collector only when the version has it.
services="api worker scanner web db trade-flow"
with="$(images_of_version | tr '\n' ' ')"
services="api worker scanner web db"
without="$(images_of_version | tr '\n' ' ')"
[[ "$with" == "api worker scanner web trade-flow " && "$without" == "api worker scanner web " ]]
check "the images of a version have the collector only when the version runs it"

if ((failures > 0)); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
