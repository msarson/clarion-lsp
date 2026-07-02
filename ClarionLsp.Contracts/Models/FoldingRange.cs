namespace ClarionLsp.Contracts.Models
{
    /// <summary>A collapsible region reported by the language server (LSP textDocument/foldingRange).</summary>
    public class FoldingRange
    {
        /// <summary>0-based first line of the fold.</summary>
        public int StartLine { get; set; }

        /// <summary>0-based last line of the fold.</summary>
        public int EndLine { get; set; }

        /// <summary>Optional kind: "comment", "imports", or "region" (may be null).</summary>
        public string Kind { get; set; }
    }
}
