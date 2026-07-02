namespace ClarionLsp.Contracts.Models
{
    /// <summary>
    /// A code lens (LSP textDocument/codeLens): an actionable annotation shown at <see cref="Range"/>.
    /// Clarion's reference-count lenses resolve their <see cref="Command"/> lazily — a lens returned by
    /// <c>GetCodeLensesAsync</c> may have a null Command until passed to <c>ResolveCodeLensAsync</c>.
    /// </summary>
    public class CodeLensResult
    {
        /// <summary>Where the lens is anchored (0-based).</summary>
        public Range Range { get; set; }

        /// <summary>The command shown/invoked by the lens, or null until resolved.</summary>
        public CommandInfo Command { get; set; }

        /// <summary>Opaque server payload round-tripped on resolve — do not interpret; pass back
        /// unchanged via <c>ResolveCodeLensAsync</c>. Null when the lens needs no resolution.</summary>
        public object Data { get; set; }
    }
}
