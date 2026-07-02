# Changelog

All notable changes to ClarionLsp will be documented in this file.

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
