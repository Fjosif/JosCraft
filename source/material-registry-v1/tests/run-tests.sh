#!/usr/bin/env bash
# Run from a COPY outside the JOSCRAFT project tree (Godot/.NET compiles every *.cs inside it).
#   cp -r collaboration/incoming/claude-material-registry /tmp/mr && cd /tmp/mr && bash tests/run-tests.sh <path-to-JOSCRAFT>
set -euo pipefail
JOS="${1:?path to JOSCRAFT project}"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
for f in "$HERE"/src/*.cs.txt; do cp "$f" "${f%.txt}"; done
cp "$HERE/tests/RegistryTests.cs.txt" "$HERE/tests/RegistryTests.cs"; cp "$HERE/tests/RegistryTests.csproj.txt" "$HERE/tests/RegistryTests.csproj"
python3 "$HERE/tools/validate_schema.py" "$HERE/material-registry.json" "$HERE/registry-schema.json"
(cd "$HERE/tests" && dotnet run -- "$HERE/material-registry.json" "$JOS/data/recipe-catalog.json")
