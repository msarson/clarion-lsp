namespace ClarionLsp.Contracts.Models
{
    public class CompletionResult
    {
        public string Label { get; set; }
        public string Kind { get; set; }
        public string Detail { get; set; }
        public string Documentation { get; set; }
        public string InsertText { get; set; }
    }
}
