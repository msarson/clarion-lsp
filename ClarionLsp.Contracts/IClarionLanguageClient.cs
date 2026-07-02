using System;
using System.Threading.Tasks;
using ClarionLsp.Contracts.Models;

namespace ClarionLsp.Contracts
{
    public interface IClarionLanguageClient
    {
        bool IsRunning { get; }

        Task<HoverResult> GetHoverAsync(string filePath, int line, int character, int timeoutMs = 3000);
        Task<LocationResult[]> GetDefinitionAsync(string filePath, int line, int character);

        /// <summary>Implementation location(s) for the symbol (LSP textDocument/implementation). For Clarion,
        /// this resolves a method's declaration (.inc) to its implementation body (.clw). 0-based coords.</summary>
        Task<LocationResult[]> GetImplementationAsync(string filePath, int line, int character);

        Task<LocationResult[]> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true);
        Task<SymbolResult[]> GetDocumentSymbolsAsync(string filePath);
        Task<SymbolResult[]> FindWorkspaceSymbolAsync(string query);

        /// <summary>
        /// Collapsible regions for the file (LSP textDocument/foldingRange). Lines are 0-based. For
        /// in-memory/synthetic buffers, push the buffer via <see cref="NotifyBufferChangedAsync"/> first.
        /// </summary>
        Task<FoldingRange[]> GetFoldingRangesAsync(string filePath);

        /// <summary>Returns the range of the symbol under the cursor, or null if it cannot be renamed.</summary>
        Task<Range> PrepareRenameAsync(string filePath, int line, int character);

        /// <summary>Renames the symbol under the cursor to <paramref name="newName"/> across the workspace.</summary>
        Task<RenameEdit[]> RenameAsync(string filePath, int line, int character, string newName);

        /// <summary>
        /// Code completion at a position. Pass <paramref name="bufferText"/> with the live
        /// (unsaved) editor contents to get scope-aware completion against in-memory text;
        /// leave it null to complete against the file on disk.
        /// </summary>
        Task<CompletionResult[]> GetCompletionAsync(string filePath, int line, int character, string bufferText = null, int timeoutMs = 3000);

        /// <summary>
        /// Parameter hints for the call at a position (LSP textDocument/signatureHelp). Triggered
        /// while typing a call's arguments ('(' and ',' are the natural triggers). Pass
        /// <paramref name="bufferText"/> to resolve against the live unsaved buffer. Returns null
        /// when the cursor is not inside a resolvable call.
        /// </summary>
        Task<SignatureHelpResult> GetSignatureHelpAsync(string filePath, int line, int character, string bufferText = null, int timeoutMs = 3000);

        /// <summary>Occurrences of the symbol under the cursor within the file (LSP
        /// textDocument/documentHighlight), for highlight-all-references-in-file. 0-based coords.</summary>
        Task<DocumentHighlight[]> GetDocumentHighlightsAsync(string filePath, int line, int character);

        /// <summary>
        /// Whole-document formatting edits (LSP textDocument/formatting). Returns the text edits the
        /// caller should apply; an empty array means the server had nothing to change.
        /// </summary>
        Task<TextEdit[]> FormatDocumentAsync(string filePath, int tabSize = 4, bool insertSpaces = false);

        /// <summary>
        /// Smart-selection ranges (LSP textDocument/selectionRange) for each supplied position — the
        /// nested syntactic ranges used by expand/shrink-selection. The result is parallel to
        /// <paramref name="positions"/> (one <see cref="SelectionRange"/> chain per position).
        /// </summary>
        Task<SelectionRange[]> GetSelectionRangesAsync(string filePath, Position[] positions);

        /// <summary>
        /// Code actions / quick-fixes available for a range (LSP textDocument/codeAction). The
        /// diagnostics overlapping the range are attached automatically as context, so the caller
        /// need only supply the range. Most Clarion actions carry a <see cref="CodeActionResult.Command"/>
        /// to run via <see cref="ExecuteCommandAsync"/>.
        /// </summary>
        Task<CodeActionResult[]> GetCodeActionsAsync(string filePath, Range range);

        /// <summary>Code lenses for the file (LSP textDocument/codeLens). Lenses whose command is
        /// computed lazily come back with a null command — resolve them via <see cref="ResolveCodeLensAsync"/>.</summary>
        Task<CodeLensResult[]> GetCodeLensesAsync(string filePath);

        /// <summary>Resolve a lazily-computed code lens (LSP codeLens/resolve), filling in its
        /// <see cref="CodeLensResult.Command"/>. Pass a lens returned by <see cref="GetCodeLensesAsync"/> unchanged.</summary>
        Task<CodeLensResult> ResolveCodeLensAsync(CodeLensResult lens);

        /// <summary>
        /// Execute a server command (LSP workspace/executeCommand), e.g. a code action's command. The
        /// server performs the work and applies any resulting changes by raising
        /// <see cref="ApplyEditRequested"/> (auto-acknowledged by the addin). Returns true if the
        /// request completed without error/timeout.
        /// </summary>
        Task<bool> ExecuteCommandAsync(string command, object[] arguments);

        /// <summary>
        /// Latest diagnostics for the file. Triggers a fresh server analysis and waits up to
        /// <paramref name="timeoutMs"/> for the resulting publish. Pass <paramref name="bufferText"/>
        /// to analyze live unsaved content. An empty array means the server reported a clean file.
        /// </summary>
        Task<DiagnosticResult[]> GetDiagnosticsAsync(string filePath, string bufferText = null, int timeoutMs = 3000);

        /// <summary>
        /// Push the live (unsaved) editor buffer to the server so subsequent requests and
        /// diagnostics reflect it. No-op if the text is unchanged since the last sync, so it
        /// is cheap to call on an editor change/idle timer.
        /// </summary>
        Task NotifyBufferChangedAsync(string filePath, string bufferText);

        /// <summary>
        /// Raised whenever the server publishes diagnostics for a file (push model, for live
        /// squiggles). Arguments are the file path and the diagnostics for it (empty = clean).
        /// </summary>
        event Action<string, DiagnosticResult[]> DiagnosticsPublished;

        /// <summary>
        /// Raised when the server asks the client to apply a workspace edit (LSP workspace/applyEdit),
        /// typically as the effect of an <see cref="ExecuteCommandAsync"/> call. The addin
        /// auto-acknowledges the server; a subscriber applies the edits to the live buffers/files.
        /// </summary>
        event Action<WorkspaceApplyEdit> ApplyEditRequested;
    }
}
