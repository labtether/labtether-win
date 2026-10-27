# Build from source

Requires Visual Studio 2026 with the .NET 10 SDK selected by `global.json`
and Windows App SDK build tools, plus Go 1.27.3 and a sibling
`labtether-agent` checkout. Release builds fail closed if
the matching Go child or its version marker is missing.

```powershell
git clone https://github.com/labtether/labtether-agent ..\labtether-agent
bash .\scripts\build-bundled-agent.sh --version dev
msbuild src\LabTetherAgent\LabTetherAgent.csproj -t:Build -p:Configuration=Release -p:Platform=x64
.\scripts\build-unpackaged.ps1 -Arch x64 -OutputDir build
```

`build-unpackaged.ps1` produces the same self-contained folder layout used by
the local release lane. Add `-Msi` to also build the per-user installer
(`installer/`, WiX Toolset v5) next to that folder, or run
`.\scripts\build-msi.ps1 -PayloadDir build\win-x64 -OutputPath build\labtether-agent-win-x64.msi`.
Both check that the MSI holds exactly the payload's files. The legacy
`build-msix.ps1` name remains as a compatibility wrapper, but it does not
produce an MSIX.

`AGENT_VERSION` names the Go agent release used by
`scripts/download-agent.ps1` for local builds. Official releases always bundle
the `labtether-agent` build from the tag that matches the wrapper tag.

For most users, download the pre-built signed installer from [Releases](https://github.com/labtether/labtether-win/releases/latest) instead.
