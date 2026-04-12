namespace ClarionLsp.Contracts.Models
{
    public class SymbolResult
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string FilePath { get; set; }
        public Range Range { get; set; }
        public string ContainerName { get; set; }
    }
}
