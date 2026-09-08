# Changelog

All notable changes to ClarionLsp will be documented in this file.

## [1.4.2] - 2026-09-08

### Fixed
- `GetDiagnosticsAsync` / `GetDiagnostics` returned the **first** `textDocument/publishDiagnostics` for a file instead of the finished analysis ([#5](https://github.com/msarson/clarion-lsp/issues/5)). The Clarion server analyses in two phases and publishes once per phase — the sync validators first, then the async ones (undeclared-variable, missing-include, discarded-return, missing-implementation) — so the value returned was routinely the incomplete first pass, reported as complete. A caller asking "is this file clean?" was told yes while the real answer was still in flight. Surfaced by @peterparker57, whose tooling read `count: 0` on the first query of every file. The wait now watches a per-file publish counter and returns once the stream has stayed quiet for `DiagnosticsSettleMs` (default 400ms), still bounded by the call's own `timeoutMs`, so a timeout returns the best set known so far exactly as before. Deliberately **not** keyed on `clarion/symbolsRefreshed`: the server sends that immediately after kicking validation off rather than when it finishes, and a file under `libsrcPaths` gets one publish only — so waiting for a second would hang there and fire early everywhere else.

### Added
- `ClarionLspService.DiagnosticsSettleMs` — the quiet period above, tunable by hosts that know their server's timing. Deliberately not on `IClarionLanguageClient`, so the contract is unchanged; set it to 0 to restore the previous return-on-first-publish behaviour.

### Changed
- `IClarionLanguageClient.GetDiagnosticsAsync` documentation now states the two-phase reality: an empty array means a clean file only when the call returned inside its budget, and `DiagnosticsPublished` remains the push-model alternative for callers that want each phase as it lands.

## [1.4.1] - 2026-07-16

### Fixed
- `GetDocumentSymbolsAsync` now preserves the full document-symbol hierarchy. `ParseDocumentSymbols` recursed only the top-level result and dropped `children`, collapsing a hierarchical outline (procedures with nested `WINDOW`/controls, classes with methods, routines with data) to just the root callables. It now recurses `children` and captures `detail` (control `USE(...)`, method signatures), reading the range from either `DocumentSymbol.range` or `SymbolInformation.location.range`.

### Added
- `SymbolResult.Children` (nested symbols) and `SymbolResult.Detail`. These are written via reflection so that if an older `ClarionLsp.Contracts` (same `AssemblyVersion 1.0.0.0`) wins the shared first-load race, `GetDocumentSymbolsAsync` degrades to a flat result rather than faulting on a missing member.

## [1.4.0] - 2026-07-02

### Added
- `IClarionLanguageClient.GetSignatureHelpAsync` — parameter hints for a call site (`textDocument/signatureHelp`), buffer-aware; `SignatureHelpResult`/`SignatureInfo`/`SignatureParameter` DTOs (LSP parameter-label offset pairs are resolved to their substring)
- `IClarionLanguageClient.GetDocumentHighlightsAsync` — occurrences of the symbol under the cursor within a file (`textDocument/documentHighlight`); `DocumentHighlight` DTO
- `IClarionLanguageClient.FormatDocumentAsync` — whole-document formatting edits (`textDocument/formatting`); `TextEdit` DTO
- `IClarionLanguageClient.GetSelectionRangesAsync` — smart expand/shrink selection ranges (`textDocument/selectionRange`); `SelectionRange` DTO
- `IClarionLanguageClient.GetCodeActionsAsync` — quick-fixes/refactors for a range (`textDocument/codeAction`); the diagnostics overlapping the range are attached as context automatically (with their server `data` intact) so data-driven Clarion actions resolve; `CodeActionResult`/`CommandInfo`/`WorkspaceEditChange` DTOs
- `IClarionLanguageClient.GetCodeLensesAsync` + `ResolveCodeLensAsync` — code lenses with lazy command resolution (`textDocument/codeLens`, `codeLens/resolve`); `CodeLensResult` DTO
- `IClarionLanguageClient.ExecuteCommandAsync` — run a server command (`workspace/executeCommand`), e.g. a code action's command
- `IClarionLanguageClient.ApplyEditRequested` event — the server's `workspace/applyEdit` reverse-request (typically the effect of a command); the addin auto-acknowledges the server and a subscriber applies the edits; `WorkspaceApplyEdit` DTO
- `LspClient` now distinguishes server→client **requests** from responses (checks `method` before `id`), answering `workspace/applyEdit` and defaulting other server requests to a null result so the server never blocks

## [1.3.0] - 2026-07-02

### Added
- `IClarionLanguageClient.GetFoldingRangesAsync` — collapsible regions for a file (`textDocument/foldingRange`); `FoldingRange` DTO
- `IClarionLanguageClient.GetImplementationAsync` — implementation location(s) for a symbol (`textDocument/implementation`), resolving a method's `.inc` declaration to its `.clw` body

## [1.1.0] - 2026-06-12

### Added
- `IClarionLanguageClient.GetCompletionAsync` — code completion at a position, with an optional `bufferText` parameter for scope-aware completion against live/unsaved editor content
- `IClarionLanguageClient.GetDiagnosticsAsync` — triggers a fresh analysis and returns the server's diagnostics for a file (with optional `bufferText`); empty array means a clean file
- `IClarionLanguageClient.NotifyBufferChangedAsync` — push live unsaved buffer text to the server (didOpen/full didChange) so requests and diagnostics reflect in-memory edits; no-op when unchanged
- `IClarionLanguageClient.DiagnosticsPublished` event — push model for live squiggles, raised on every `textDocument/publishDiagnostics`
- `CompletionResult` and `DiagnosticResult` DTOs in `ClarionLsp.Contracts.Models`
- `LspClient` now dispatches server-initiated `textDocument/publishDiagnostics` notifications (previously only request/response messages were handled) and tracks per-document versions for incremental `didChange`

## [1.0.0] - 2026-03-17

### Added
- Initial public release
- LSP server lifecycle management — auto-discovers the highest installed version of `msarson.clarion-extensions` from `%USERPROFILE%\.vscode\extensions`
- `IClarionLanguageClient` public interface in `ClarionLsp.Contracts.dll` (no SharpDevelop dependency) so other addins can consume LSP features without coupling to SharpDevelop
- Hover support — rich hover tooltips for procedures, classes, variables, and built-in functions
- Go to Definition — navigate to the declaration of any symbol
- Go to Implementation — navigate to the implementation of methods and procedures
- Find All References — list all usages of a symbol across the solution
- Workspace Symbols — quick-open any procedure or class in the workspace
- Rename — rename a symbol and all its references across all open files
- Separate `ClarionLsp.Contracts.dll` deployment so consumer addins can reference the interface without the full SharpDevelop-coupled implementation
