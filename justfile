version := `grep -oP 'Version = "\K[^"]+' src/Plugin.cs`
name := "ValheimPrometheusExporter"

# Build the plugin DLL (game and BepInEx DLLs must be in lib/, see README)
build:
    dotnet build src/{{name}}.csproj -c Release

# Build and assemble dist/{{name}}-<version>.zip: the DLL, this README and the manifest
package: build
    #!/usr/bin/env bash
    set -euo pipefail
    grep -q '"version_number": "{{version}}"' package/manifest.json \
      || { echo "package/manifest.json version_number is not {{version}}"; exit 1; }
    stage="dist/{{name}}"
    rm -rf "$stage" && mkdir -p "$stage"
    cp "$(find src/bin/Release -name '{{name}}.dll' | head -1)" README.md package/manifest.json "$stage/"
    rm -f "dist/{{name}}-{{version}}.zip"
    (cd "$stage" && zip -q -r "../{{name}}-{{version}}.zip" .)
    unzip -l "dist/{{name}}-{{version}}.zip"
