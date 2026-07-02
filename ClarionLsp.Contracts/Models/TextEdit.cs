namespace ClarionLsp.Contracts.Models
{
    /// <summary>A single text replacement (LSP TextEdit): replace <see cref="Range"/> with
    /// <see cref="NewText"/>. Used by document formatting and workspace edits.</summary>
    public class TextEdit
    {
        /// <summary>The range to replace (0-based). An empty range is an insertion.</summary>
        public Range Range { get; set; }

        /// <summary>The replacement text (may be empty for a deletion).</summary>
        public string NewText { get; set; }
    }

    /// <summary>The text edits targeting a single file, as part of a larger workspace edit.</summary>
    public class WorkspaceEditChange
    {
        /// <summary>Absolute path of the file the edits apply to.</summary>
        public string FilePath { get; set; }

        /// <summary>The edits to apply to <see cref="FilePath"/> (never null; may be empty).</summary>
        public TextEdit[] Edits { get; set; }
    }
}
