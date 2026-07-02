namespace ClarionLsp.Contracts.Models
{
    /// <summary>A smart-selection range (LSP textDocument/selectionRange). Selecting outward
    /// (expand) walks up the <see cref="Parent"/> chain; selecting inward (shrink) walks down.</summary>
    public class SelectionRange
    {
        /// <summary>The range at this level (0-based).</summary>
        public Range Range { get; set; }

        /// <summary>The enclosing selection range, or null at the outermost level.</summary>
        public SelectionRange Parent { get; set; }
    }
}
