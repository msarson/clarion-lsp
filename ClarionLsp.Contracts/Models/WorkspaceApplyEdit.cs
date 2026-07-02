namespace ClarionLsp.Contracts.Models
{
    /// <summary>
    /// Edits the server asks the client to apply (LSP workspace/applyEdit reverse-request), raised via
    /// <c>IClarionLanguageClient.ApplyEditRequested</c>. Typically arrives while an
    /// <c>ExecuteCommandAsync</c> call is in flight (e.g. a Clarion "Add Constants" quick-fix). The
    /// addin auto-acknowledges the server; a subscriber is responsible for applying these edits to the
    /// live buffers/files.
    /// </summary>
    public class WorkspaceApplyEdit
    {
        /// <summary>Optional label describing the edit (may be null).</summary>
        public string Label { get; set; }

        /// <summary>Per-file edits to apply (never null; may be empty).</summary>
        public WorkspaceEditChange[] Changes { get; set; }
    }
}
