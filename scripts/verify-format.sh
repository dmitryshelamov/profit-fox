#!/bin/sh
# Verifies the format of a solution as `dotnet format --verify-no-changes` does, with one exception.
#
# The code style writes every attribute in brackets of its own, with a space between them on one line:
#     [FromQuery] [BindRequired] string interval
# The formatter of Roslyn deletes that space and has no option to keep it, so `dotnet format` alone would reject the style. This
# script lets exactly that report pass (WHITESPACE, "Delete 1 characters", at a space between `]` and `[`) and fails on any other.
#
# Usage: scripts/verify-format.sh <solution>
set -u

solution=${1:?"Usage: $0 <solution>"}

output=$(dotnet format "$solution" --verify-no-changes --no-restore 2>&1)
status=$?

if [ "$status" -eq 0 ]; then
    exit 0
fi

# A report is `<path>(<line>,<column>): error <id>: <message> [<project>]`; the column of the tolerated one is the space.
reports=$(printf '%s\n' "$output" | grep -E '\([0-9]+,[0-9]+\): (error|warning) ' || true)

if [ -z "$reports" ]; then
    # dotnet format failed without telling where: show what it said.
    printf '%s\n' "$output"
    exit "$status"
fi

rejected=$(printf '%s\n' "$reports" | awk '
    {
        match($0, /\([0-9]+,[0-9]+\): (error|warning) /)
        path = substr($0, 1, RSTART - 1)
        split(substr($0, RSTART + 1), position, /[,)]/)
        line = position[1]
        column = position[2]

        if (index($0, "WHITESPACE: Fix whitespace formatting. Delete 1 characters.") > 0) {
            text = ""
            command = "sed -n " line "p \"" path "\""
            command | getline text
            close(command)

            if (substr(text, column - 1, 3) == "] [") {
                next
            }
        }

        print
    }' | sort -u)

if [ -n "$rejected" ]; then
    printf '%s\n' "$rejected"
    exit 1
fi
