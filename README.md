# ClarionLsp

A shared SharpDevelop addin that manages the [Clarion Language Server](https://github.com/msarson/clarion-extensions) lifecycle inside the Clarion IDE, and exposes a clean public API (`IClarionLanguageClient`) so any other addin can query hover text, go-to-definition, find-references, and document/workspace symbols — without knowing anything about LSP internals.

---

## Overview

```
ClarionLsp.Contracts   ← public API dll — reference this from consumer addins
ClarionLsp             ← the addin dll — manages server process + JSON-RPC
```

`ClarionLsp.Contracts` contains only interfaces and DTOs. Consumer addins reference it at **compile time** but do **not** need to reference `ClarionLsp.dll`. At runtime they use `ClarionLspLocator.Current` (populated by the addin) to call the language server.

---

## Features

| Capability | LSP Method |
|---|---|
| Hover / tooltip | `textDocument/hover` |
| Go to definition | `textDocument/definition` |
| Find all references | `textDocument/references` |
| Document symbols | `textDocument/documentSymbol` |
| Workspace symbol search | `workspace/symbol` |

---

## Architecture

### Server discovery (in priority order)

1. **Configured path** — `PropertyService.Get("Lsp.ServerPath")` in the Clarion IDE preferences
2. **Bundled server** — `<addin-dir>\lsp-server\out\server\src\server.js` (copy the VS Code extension's built `out/` tree here)
3. **Auto-discovered** — highest version of `msarson.clarion-extensions-*` found in `%USERPROFILE%\.vscode\extensions\`

If none of these resolve to a real file the addin logs a diagnostic and does nothing — it does not throw or crash the IDE.

### Startup flow

`LspStartupCommand.Run()` is called by SharpDevelop's autostart mechanism when the IDE loads. It:

1. Hooks `ProjectService.SolutionLoaded` and `SolutionClosed`
2. Starts the LSP server immediately (standalone mode if no solution is open)
3. Sends `clarion/updatePaths` with the version config resolved via reflection from `Clarion.Core.Options.Versions`
4. Sets `ClarionLspLocator.Current` so consumer addins can start calling it

On `SolutionLoaded` it either starts the server (if not running) or re-sends updated paths with the new solution context. On `SolutionClosed` it sends an empty-project `updatePaths` so the server's index is cleared.

### Version detection

`ClarionVersionService.Detect()` uses reflection on `Clarion.Core.dll` to call `Versions.GetVersion(true)` — this returns the IDE's live ClarionVersion object containing:

- `Name` — e.g. `"Clarion11.1"`
- `Path` — the Clarion bin directory
- `Libsrc` — semicolon-delimited library source paths
- `RedirectionFile.Name` + `RedirectionFile.Macros` — the active redirection file and its macro table

All of this is forwarded to the language server via `clarion/updatePaths` so it can resolve `%REDIRECTION_MACRO%`-expanded paths.

### Logging

All log output goes to `OutputDebugString` (visible in [DebugView](https://learn.microsoft.com/en-us/sysinternals/downloads/debugview) or any kernel debugger). Each line is prefixed with `[ClarionLsp]`.

---

## Build & deploy

### Prerequisites

- Visual Studio 2022 (for MSBuild 17)
- .NET Framework 4.8
- Clarion IDE installed (for the `$(ClarionBin)` reference — see below)

### ClarionBin property

Both projects reference Clarion IDE assemblies via `$(ClarionBin)`. Set this in `Directory.Build.props` or `Directory.Build.props.user` in the solution root:

```xml
<Project>
  <PropertyGroup>
    <ClarionBin>C:\Clarion\Clarion11.1\bin</ClarionBin>
  </PropertyGroup>
</Project>
```

Adjust the path to match your Clarion installation.

### Build

```powershell
# dotnet CLI (simplest)
dotnet build ClarionLsp.slnx -c Debug

# or MSBuild directly
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    ClarionLsp.slnx /p:Configuration=Debug /v:minimal
```

### Automatic deploy on build

`ClarionLsp.csproj` includes a `DeployAddin` target that copies the built DLL and `.addin` manifest to:

```
C:\Clarion\Clarion11.1\accessory\addins\ClarionLsp\
```

`ClarionLsp.Contracts.csproj` copies `ClarionLsp.Contracts.dll` to `$(ClarionBin)` so it is on the IDE's probing path and consumer addins can load it.

The Clarion IDE must be closed before deploying — DLLs are locked while the IDE runs.

---

## Consuming the API from another addin

### 1. Reference `ClarionLsp.Contracts.dll`

Add a reference in your `.csproj`:

```xml
<Reference Include="ClarionLsp.Contracts">
  <HintPath>$(ClarionBin)\ClarionLsp.Contracts.dll</HintPath>
  <Private>False</Private>
</Reference>
```

Set `<Private>False</Private>` so the DLL is not copied into your output — it lives in the IDE bin directory.

### 2. Declare a soft dependency in your `.addin` manifest

```xml
<Dependency addin="ClarionLsp" version="1.0" coerced="true"/>
```

`coerced="true"` means your addin still loads even if ClarionLsp is absent — always null-check `ClarionLspLocator.Current`.

### 3. Call the API

```csharp
using ClarionLsp.Contracts;
using ClarionLsp.Contracts.Models;

// Hover (0-based line/character — LSP convention)
var client = ClarionLspLocator.Current;
if (client == null || !client.IsRunning) return;

HoverResult hover = await client.GetHoverAsync(filePath, line, character);
if (hover != null)
    ShowTooltip(hover.Contents);

// Go to definition
LocationResult[] defs = await client.GetDefinitionAsync(filePath, line, character);
foreach (var def in defs)
    NavigateTo(def.FilePath, def.Range.Start.Line);

// Find all references
LocationResult[] refs = await client.GetReferencesAsync(filePath, line, character);

// Document symbols (outline)
SymbolResult[] symbols = await client.GetDocumentSymbolsAsync(filePath);

// Workspace symbol search
SymbolResult[] results = await client.FindWorkspaceSymbolAsync("MyClass");
```

### Line/character numbering

All coordinates are **0-based** (LSP convention). The Clarion IDE's SharpDevelop text editor uses 0-based lines internally (`IDocument.GetLineSegment`) but some UI elements display 1-based. Convert accordingly before calling.

---

## Public API reference

### `ClarionLspLocator`

```csharp
public static class ClarionLspLocator
{
    public static IClarionLanguageClient Current { get; set; }
}
```

Set by `ClarionLsp` on startup. `null` when the addin is not loaded or the server failed to start.

### `IClarionLanguageClient`

```csharp
public interface IClarionLanguageClient
{
    bool IsRunning { get; }

    Task<HoverResult>      GetHoverAsync(string filePath, int line, int character, int timeoutMs = 3000);
    Task<LocationResult[]> GetDefinitionAsync(string filePath, int line, int character);
    Task<LocationResult[]> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true);
    Task<SymbolResult[]>   GetDocumentSymbolsAsync(string filePath);
    Task<SymbolResult[]>   FindWorkspaceSymbolAsync(string query);
}
```

### `HoverResult`

| Property | Type | Description |
|---|---|---|
| `Contents` | `string` | Markdown or plain-text hover content from the language server |
| `Range` | `Range` | The word range the hover applies to (may be null) |

### `LocationResult`

| Property | Type | Description |
|---|---|---|
| `FilePath` | `string` | Absolute file path (backslash-normalised) |
| `Range` | `Range` | 0-based start/end position |

### `SymbolResult`

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Symbol name |
| `Kind` | `string` | Human-readable kind: `"Class"`, `"Method"`, `"Variable"`, etc. |
| `FilePath` | `string` | Absolute file path |
| `Range` | `Range` | Symbol location |
| `ContainerName` | `string` | Enclosing class/procedure name (may be null) |

### `Range` / `Position`

```csharp
public class Range    { public Position Start; public Position End; }
public class Position { public int Line; public int Character; }  // 0-based
```

---

## Project structure

```
clarion-lsp/
├── ClarionLsp.slnx                  solution file
├── ClarionLsp/
│   ├── ClarionLsp.addin             SharpDevelop addin manifest
│   ├── ClarionLsp.csproj
│   ├── LspStartupCommand.cs         addin entry point, lifecycle management
│   ├── ClarionLspService.cs         IClarionLanguageClient implementation + response parsers
│   ├── LspClient.cs                 raw JSON-RPC over stdio transport
│   ├── ClarionVersionService.cs     reflection-based Clarion version detection
│   └── Ods.cs                       OutputDebugString logging helper
└── ClarionLsp.Contracts/
    ├── ClarionLsp.Contracts.csproj
    ├── IClarionLanguageClient.cs    public interface
    ├── ClarionLspLocator.cs         service locator
    └── Models/
        ├── HoverResult.cs
        ├── LocationResult.cs
        └── SymbolResult.cs
```

---

## Notes for Clarion IDE addin developers

- **`!!!` doc comments** — The Clarion language server reads `!!!` doc comments from `.inc`/`.clw` files. Triple-bang (`!!!`) marks a doc comment; quadruple-bang (`!!!!`) is treated as a regular comment. Comments before a declaration attach to that symbol. For methods, the definition's `.clw` comment takes priority over the declaration's `.inc` comment if both exist.

- **Inline `!` fallback** — A regular `!` comment on the same line as a declaration is auto-wrapped as a `<summary>` if no `!!!` comment is present.

- **Builtins** — The Clarion IDE's native tooltip reads builtin documentation from `builtins2.cln` (in the Clarion install). If you bundle an LSP server, ensure it has equivalent coverage for builtins like `GET`, `SORT`, `NEXT`.

- **Language keywords** — Hover does not return results for bare language keywords (`IF`, `LOOP`, `END`, `PROCEDURE`) — the language server resolves user symbols only.

---

## Licence

[MIT](LICENSE) — © 2025 msarson
