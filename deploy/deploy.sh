#!/usr/bin/env bash
# Deploys version N of profit-fox on the production server; deploying an earlier N rolls back.
# The deploy key in ~/.ssh/authorized_keys runs only this script (a forced command), so the request comes in
# SSH_ORIGINAL_COMMAND as "deploy <N> <protocol>". The script trusts nothing else the caller sends: it takes the commit
# of tag vN from GitHub, accepts it only from main and runs only images built from that commit.
# Installed by hand as ~/profit-fox/deploy.sh (README, "Production"); it needs curl, jq and docker.

# Raised when the command changes; deploy.yml passes the same number, so an outdated copy on the server refuses.
DEPLOY_PROTOCOL=1
REPOSITORY=dmitryshelamov/profit-fox
ROOT="$HOME/profit-fox"
IMAGES=(api worker scanner web)
# The collector of the trade flow came after the first versions: a version whose compose file has no such service has no image of it, and
# its deploy leaves the collector stopped (a rollback to such a version included).
COLLECTOR=trade-flow

parse_command() {
  local pattern='^deploy ([1-9][0-9]*) ([1-9][0-9]*)$'
  if ! [[ "$1" =~ $pattern ]]; then
    echo "deploy: expected 'deploy <version> <protocol>'" >&2
    return 1
  fi
  if [[ "${BASH_REMATCH[2]}" != "$DEPLOY_PROTOCOL" ]]; then
    echo "deploy: the workflow speaks protocol ${BASH_REMATCH[2]}, this script $DEPLOY_PROTOCOL: copy deploy/deploy.sh of that commit to the server" >&2
    return 1
  fi
  echo "${BASH_REMATCH[1]}"
}

fail() {
  echo "deploy: $*" >&2
  exit 1
}

github() {
  curl --fail --silent --show-error --max-time 30 -H 'Accept: application/vnd.github+json' \
    "https://api.github.com/repos/$REPOSITORY/$1"
}

# The commit of tag vN; a missing tag, or one that names anything but a commit, is refused.
tag_commit() {
  local ref
  ref="$(github "git/ref/tags/v$1")" || fail "no tag v$1"
  jq --exit-status --raw-output 'select(.object.type == "commit") | .object.sha' <<<"$ref" ||
    fail "tag v$1 does not name a commit"
}

# main contains the commit when main is the commit itself or comes after it.
in_main() {
  local status
  status="$(github "compare/main...$1" | jq --raw-output '.status')" || return 1
  [[ "$status" == "behind" || "$status" == "identical" ]]
}

wait_for_api() {
  local deadline=$((SECONDS + 120))
  until curl --fail --silent --output /dev/null --max-time 5 http://127.0.0.1:8080/openapi/v1.json; do
    ((SECONDS < deadline)) || return 1
    sleep 2
  done
}

# The compose file of the version being deployed, with its number; main sets version and release.
compose() {
  PROFITFOX_VERSION="$version" docker compose --file "$release/docker-compose.prod.yml" \
    --env-file "$ROOT/.env.prod" "$@"
}

# Whether the compose file of the version runs the collector of the trade flow.
has_collector() {
  compose config --services | grep --quiet --line-regexp "$COLLECTOR"
}

# The images of the version: the four of every version, and the collector when the version has it.
images_of_version() {
  printf '%s\n' "${IMAGES[@]}"
  if has_collector; then
    echo "$COLLECTOR"
  fi
}

# Stops the collector whichever version runs it: the compose file of an earlier version knows no such service.
stop_collector() {
  local container
  while read -r container; do
    [[ -n "$container" ]] && docker stop "$container" >/dev/null
  done < <(docker ps --quiet --filter "label=com.docker.compose.service=$COLLECTOR")
}

# The Worker, the scanner and the collector wait until the API of this version has migrated the database; an API that does not answer leaves
# them stopped. A version without the collector leaves it stopped, and its data stay in the database.
roll_out() {
  local collector=no
  if has_collector; then
    collector=yes
  fi

  compose stop worker scanner
  stop_collector
  compose up --detach --wait db
  compose up --detach --no-deps api
  wait_for_api || fail "the API of version $version did not answer in 120 s; the Worker, the scanner and the collector stay stopped"
  compose up --detach --no-deps worker scanner web

  if [[ "$collector" == yes ]]; then
    compose up --detach --no-deps "$COLLECTOR"
  else
    echo "deploy: version $version has no collector of the trade flow; it stays stopped"
  fi
}

main() {
  set -euo pipefail

  local commit image revision
  version="$(parse_command "${SSH_ORIGINAL_COMMAND:-}")" || exit 1

  # Nothing on the server changes until the version is known to be a commit of main.
  commit="$(tag_commit "$version")"
  in_main "$commit" || fail "the commit $commit of tag v$version is not in main"
  echo "deploy: version $version is commit $commit"

  release="$ROOT/releases/$version"
  mkdir -p "$release"
  curl --fail --silent --show-error --max-time 30 --output "$release/docker-compose.prod.yml" \
    "https://raw.githubusercontent.com/$REPOSITORY/$commit/docker-compose.prod.yml"

  compose pull --quiet
  # An image pushed under an existing number from another commit does not run.
  for image in $(images_of_version); do
    image="ghcr.io/$REPOSITORY-$image:$version"
    revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$image")"
    [[ "$revision" == "$commit" ]] || fail "$image is built from '$revision', not from $commit"
  done

  roll_out

  echo "$version" >"$ROOT/current"
  # GHCR keeps every version, so an image no container uses can be pulled again.
  docker image prune --all --force --filter "until=168h"
  echo "deploy: version $version is running"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main
fi
