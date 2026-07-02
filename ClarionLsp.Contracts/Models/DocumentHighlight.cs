namespace ClarionLsp.Contracts.Models
{
    /// <summary>An occurrence of the symbol under the cursor (LSP textDocument/documentHighlight),
    /// used to highlight all reads/writes of a symbol within the current file.</summary>
    public class DocumentHighlight
    {
        /// <summary>The range of this occurrence (0-based).</summary>
        public Range Range { get; set; }

        /// <summary>"Text" | "Read" | "Write" — defaults to "Text" when the server omits a kind.</summary>
        public string Kind { get; set; }
    }
}
