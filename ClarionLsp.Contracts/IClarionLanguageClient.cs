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
        Task<LocationResult[]> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true);
        Task<SymbolResult[]> GetDocumentSymbolsAsync(string filePath);
        Task<SymbolResult[]> FindWorkspaceSymbolAsync(string query);

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
    }
}
