#!/bin/bash
# PreToolUse hook for Bash: blocks git commands that bypass pull requests or destroy work.
# Exit code 2 blocks the tool call and shows stderr to the agent.

command=$(jq -r '.tool_input.command // empty')
[[ "$command" == *git* ]] || exit 0

block() {
  echo "BLOCKED by .claude/hooks/block-dangerous-git.sh: $1. Work in a feature branch and merge through a GitHub pull request; ask the owner if this command is really needed." >&2
  exit 2
}

# `git` plus any global options before the subcommand, e.g. `git -C . push`, `git -c k=v --no-pager reset`.
git='git([[:space:]]+(-[Cc]|--git-dir|--work-tree|--namespace)[[:space:]]+[^[:space:]]+|[[:space:]]+-[^[:space:]]+)*[[:space:]]+'

patterns=(
  "${git}"'push.*(--force|--force-with-lease|--mirror|--delete|[[:space:]]-f([[:space:]]|$))|force push or remote deletion'
  "${git}"'push.*([[:space:]]|:|\+)main([[:space:]]|$)|push to main'
  "${git}"'reset[[:space:]]+.*--hard|git reset --hard'
  "${git}"'clean[[:space:]]+(.*[[:space:]])?-[a-zA-Z]*f|git clean -f'
  "${git}"'branch[[:space:]]+(.*[[:space:]])?-D|git branch -D'
  "${git}"'(checkout|restore)[[:space:]]+(--[[:space:]]+)?\.([[:space:]]|$)|discarding all local changes'
  "${git}"'(commit|push).*--no-verify|skipping git hooks'
)

for entry in "${patterns[@]}"; do
  if [[ "$command" =~ ${entry%|*} ]]; then
    block "${entry##*|}"
  fi
done

if [[ "$command" =~ ${git}(commit|push) ]]; then
  branch=$(git -C "${CLAUDE_PROJECT_DIR:-.}" branch --show-current 2>/dev/null)
  [[ "$branch" == "main" ]] && block "commit or push while on main"
fi

exit 0
