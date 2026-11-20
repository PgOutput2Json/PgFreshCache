#!/usr/bin/env bash
# Builds the PgFreshCache.Lite package in Release, at the version in PgFreshCache.Lite/PgFreshCache.Lite.csproj, and pushes
# it to nuget.org. Prompts for the API key; it's never written to disk. The symbols (.snupkg) are pushed with it.
set -euo pipefail

cd "$(dirname "$0")"

project=PgFreshCache.Lite

version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$project/$project.csproj")
[[ -n "$version" ]] || { echo "No <Version> in $project/$project.csproj" >&2; exit 1; }

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "The working tree has changes: commit or stash them first, so the package matches a commit." >&2
    exit 1
fi

tag="v$version"
if [[ "$(git rev-parse -q --verify "$tag^{commit}" || true)" != "$(git rev-parse HEAD)" ]]; then
    echo "Warning: HEAD isn't tagged $tag."
    read -rp "Publish $version anyway? [y/N] " answer
    [[ "$answer" == [yY] ]] || exit 1
fi

package="$project/bin/Release/$project.$version.nupkg"

# GeneratePackageOnBuild: the build creates the package (and validates it against the baseline release). The old one is
# removed first, so a failed build can't leave a stale package to push
rm -f "$package" "${package%.nupkg}.snupkg"

dotnet build -c Release --disable-build-servers "$project/$project.csproj"

[[ -f "$package" ]] || { echo "Missing $package" >&2; exit 1; }

read -rsp "nuget.org API key for $project $version: " api_key
echo
[[ -n "$api_key" ]] || { echo "No API key: nothing pushed." >&2; exit 1; }

dotnet nuget push "$package" --api-key "$api_key" --source https://api.nuget.org/v3/index.json --skip-duplicate

unset api_key

echo "Pushed $project $version."
