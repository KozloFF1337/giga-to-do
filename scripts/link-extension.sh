#!/usr/bin/env sh
# Execute on the target computer only. This script has not been run against Gigacode.
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(dirname "$script_dir")
exec "${GIGA_CLI:-gigacode}" extensions link "$repo_dir/extensions/giga-todo"
