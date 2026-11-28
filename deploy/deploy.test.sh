#!/usr/bin/env bash
# Tests of the command parsing of deploy.sh, the only input the deploy key can pass to the server, a copy of the tests of
# profit-fox-market-data. Run: bash deploy/deploy.test.sh
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

role_file="$(mktemp)"
role_is() {
  local content="$1" expected="$2" actual
  if [[ "$content" == "<none>" ]]; then rm -f "$role_file"; else printf '%s' "$content" >"$role_file"; fi
  if actual="$(read_role "$role_file" 2>/dev/null)"; then :; else actual="<refused>"; fi
  if [[ "$actual" == "$expected" ]]; then
    echo "ok: role '$content' gives $expected"
  else
    echo "FAIL: role '$content' should give $expected, got '$actual'"
    failures=$((failures + 1))
  fi
}
role_is "<none>" main
role_is "main" main
role_is "workers" workers
role_is "workers"$'\n' workers
role_is "Workers" "<refused>"
role_is "workers; id" "<refused>"
role_is "" "<refused>"
rm -f "$role_file"

# The secrets of a role (change add-exchange-accounts, task 7.1): the main machine needs the master key of the API as well, and a
# missing secret is named without any value of the file.
env_file="$(mktemp)"
secrets_are() {
  local content="$1" role="$2" expected="$3" actual error
  printf '%s' "$content" >"$env_file"
  # shellcheck disable=SC2046 # the names are words without spaces
  if error="$(require_secrets "$env_file" $(secrets_of "$role") 2>&1)"; then actual="<ok>"; else actual="$error"; fi
  if [[ "$actual" == "<ok>" && "$expected" == "<ok>" ]] || [[ "$expected" != "<ok>" && "$actual" == *"has no $expected:"* ]]; then
    echo "ok: $role with '${content//$'\n'/ | }' gives $expected"
  else
    echo "FAIL: $role with '${content//$'\n'/ | }' should give $expected, got '$actual'"
    failures=$((failures + 1))
  fi
  if [[ "$actual" == *"s3cr3t"* ]]; then
    echo "FAIL: the refusal shows a value of the env file"
    failures=$((failures + 1))
  fi
}
secrets_are "PROFIT_FOX_DB_PASSWORD=ab12"$'\n'"PROFIT_FOX_MASTER_KEY=s3cr3t=="$'\n' main "<ok>"
secrets_are "PROFIT_FOX_DB_PASSWORD=s3cr3t"$'\n' main PROFIT_FOX_MASTER_KEY
secrets_are "PROFIT_FOX_DB_PASSWORD=s3cr3t"$'\n'"PROFIT_FOX_MASTER_KEY="$'\n' main PROFIT_FOX_MASTER_KEY
secrets_are "# PROFIT_FOX_MASTER_KEY=s3cr3t"$'\n'"PROFIT_FOX_DB_PASSWORD=ab12"$'\n' main PROFIT_FOX_MASTER_KEY
secrets_are "" main "PROFIT_FOX_DB_PASSWORD PROFIT_FOX_MASTER_KEY"
secrets_are "PROFIT_FOX_DB_PASSWORD=ab12"$'\n' workers "<ok>"
secrets_are "PROFIT_FOX_MASTER_KEY=s3cr3t"$'\n' workers PROFIT_FOX_DB_PASSWORD
rm -f "$env_file"

# Compose itself refuses the file of the main machine without the master key, naming it, and takes it with the key. Needs
# docker compose, which the runner of CI has.
compose_file="$(dirname "${BASH_SOURCE[0]}")/../docker-compose.prod.yml"
if docker compose version >/dev/null 2>&1; then
  if error="$(env -u PROFIT_FOX_MASTER_KEY PROFIT_FOX_VERSION=1 PROFIT_FOX_DB_PASSWORD=ab12 \
    docker compose --file "$compose_file" config --quiet 2>&1)"; then
    echo "FAIL: docker-compose.prod.yml should be refused without PROFIT_FOX_MASTER_KEY"
    failures=$((failures + 1))
  elif [[ "$error" == *PROFIT_FOX_MASTER_KEY* ]]; then
    echo "ok: docker-compose.prod.yml is refused without PROFIT_FOX_MASTER_KEY, naming it"
  else
    echo "FAIL: the refusal of docker-compose.prod.yml should name PROFIT_FOX_MASTER_KEY, got '$error'"
    failures=$((failures + 1))
  fi
  if PROFIT_FOX_VERSION=1 PROFIT_FOX_DB_PASSWORD=ab12 PROFIT_FOX_MASTER_KEY=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8= \
    docker compose --file "$compose_file" config --quiet; then
    echo "ok: docker-compose.prod.yml is taken with PROFIT_FOX_MASTER_KEY"
  else
    echo "FAIL: docker-compose.prod.yml should be taken with PROFIT_FOX_MASTER_KEY"
    failures=$((failures + 1))
  fi
else
  echo "skip: no docker compose, docker-compose.prod.yml is not checked"
fi

# Another protocol means deploy.sh on the server and deploy.yml come from different commits: the error says what to do.
error="$(parse_command "deploy 7 $((protocol + 1))" 2>&1 >/dev/null)"
if [[ "$error" == *"copy deploy/deploy.sh"* ]]; then
  echo "ok: another protocol asks to copy deploy.sh to the server"
else
  echo "FAIL: another protocol should ask to copy deploy.sh to the server"
  failures=$((failures + 1))
fi

# The commit is in the branch when the branch is the commit itself or comes after it. GitHub answers for the branch as the base;
# the slash of its name is escaped.
compare_status=""
github() {
  [[ "$1" == "compare/release%2F0.1.0...$commit" ]] && echo "{\"status\": \"$compare_status\"}"
}
commit=0123456789abcdef0123456789abcdef01234567

in_branch_gives() {
  compare_status="$1"
  if in_branch "$commit"; then actual=yes; else actual=no; fi
  if [[ "$actual" == "$2" ]]; then
    echo "ok: compare status '$1' gives in branch: $2"
  else
    echo "FAIL: compare status '$1' should give in branch: $2, got $actual"
    failures=$((failures + 1))
  fi
}

in_branch_gives behind yes
in_branch_gives identical yes
in_branch_gives ahead no
in_branch_gives diverged no

if ((failures > 0)); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
