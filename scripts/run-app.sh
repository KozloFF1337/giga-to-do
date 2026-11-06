#!/usr/bin/env sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(dirname "$script_dir")
if [ -z "${ConnectionStrings__Database:-}" ] && [ ! -f "$repo_dir/src/GigaTodo/appsettings.Local.json" ]; then
    echo 'Задайте ConnectionStrings__Database или src/GigaTodo/appsettings.Local.json с отдельной PostgreSQL-базой.' >&2
    exit 1
fi
exec "${GIGA_DOTNET:-dotnet}" run --project "$repo_dir/src/GigaTodo/GigaTodo.csproj" "$@"
