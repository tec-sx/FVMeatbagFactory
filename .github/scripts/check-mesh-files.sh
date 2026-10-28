#!/usr/bin/env bash
# Fails if a mesh or engine asset is tracked outside the folders meant for shareable (CC0) assets.
# The repository is public: private character meshes belong in a project under
# Documents/Projects or in the git-ignored assets/Private/.
# Run it locally before pushing: .github/scripts/check-mesh-files.sh
set -euo pipefail

allowed='^(assets/Samples/|tests/[^/]+/Fixtures/)'
pattern='\.(glb|gltf|fbx|obj|blend|blend1|ma|mb|max|uasset|umap|usd|usda|usdc|usdz|abc|ztl|zpr)$'

offending=$(git ls-files | grep -iE "$pattern" | grep -vE "$allowed" || true)
if [[ -n "$offending" ]]; then
  echo "These mesh/asset files are outside assets/Samples/ and test fixtures:" >&2
  echo "$offending" | sed 's/^/  /' >&2
  echo "If they are private, remove them from git (git rm --cached) and keep them in assets/Private/." >&2
  exit 1
fi
echo "No mesh files outside assets/Samples/ and test fixtures."
