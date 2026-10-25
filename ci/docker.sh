#!/bin/sh
# The `docker` check: tests.yml's `docker` job. Nothing else in CI builds the container image, and it can break on
# its own: the Dockerfile restores from a hand-listed set of project files rather than from the whole tree, so a new
# project that restore depends on is missing from the build context and fails there while every other check stays
# green. Build only, no push and no run: the point is that the image can still be produced.
#
# yggdrasil.yml runs it in the pinned Docker CLI image (Alpine: no bash, so this is POSIX sh) with the CI engine's
# socket, from the repository's root. The GitHub job's layer cache has no equivalent here; the engine's own cache does.
set -eu

# The repository's root, wherever this is run from.
cd "$(dirname "$0")/.."

tag="coding-challenge-library-check:$$"
# The image is only built to prove the Dockerfile works: remove it afterwards, whatever the outcome.
trap 'docker image rm --force "$tag" >/dev/null 2>&1 || true' EXIT

DOCKER_BUILDKIT=1 docker build --tag "$tag" .
