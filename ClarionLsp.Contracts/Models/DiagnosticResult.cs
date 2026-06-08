namespace ClarionLsp.Contracts.Models
{
    public class DiagnosticResult
    {
        /// <summary>Error | Warning | Information | Hint.</summary>
        public string Severity { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public Range Range { get; set; }
    }
}
