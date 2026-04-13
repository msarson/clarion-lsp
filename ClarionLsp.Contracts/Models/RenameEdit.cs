namespace ClarionLsp.Contracts.Models
{
    public class RenameEdit
    {
        public string FilePath { get; set; }
        public Range Range { get; set; }
        public string NewText { get; set; }
    }
}
