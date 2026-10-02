#!/usr/bin/env bash
# Build and push the three UpdateNotifier images to Docker Hub.
#
# Usage (Rider terminal / Git Bash):
#   ./publish-images.sh              build + push :latest
#   ./publish-images.sh v1.2.0       build + push :v1.2.0
#   SKIP_PUSH=1 ./publish-images.sh  build + tag only, no push
#
# Override the registry namespace with NAMESPACE=... if it ever changes.
set -euo pipefail

cd "$(dirname "$0")"

TAG="${1:-latest}"
NAMESPACE="${NAMESPACE:-infinitecanvas}"

if ! docker info >/dev/null 2>&1; then
	echo "Docker is not running." >&2
	exit 1
fi

# no login pre-check: 'docker system info' Username output is version-dependent
# and false-negatives; docker push's own auth error is the reliable signal

build_and_push() {
	local name="$1" dockerfile="$2"
	local image="$NAMESPACE/update-notifier-$name:$TAG"
	echo ""
	echo "==> $image"
	docker build -t "$image" -f "$dockerfile" .
	if [[ "${SKIP_PUSH:-0}" != "1" ]]; then
		docker push "$image"
	fi
}

build_and_push api UpdateNotifier/Dockerfile
build_and_push bot UpdateNotifier.Discord/Dockerfile
build_and_push web web/Dockerfile

echo ""
if [[ "${SKIP_PUSH:-0}" == "1" ]]; then
	echo "Done — built and tagged :$TAG (push skipped)."
else
	echo "Done — pushed :$TAG."
fi
