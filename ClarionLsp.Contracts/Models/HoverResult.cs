namespace ClarionLsp.Contracts.Models
{
    public class HoverResult
    {
        public string Contents { get; set; }
        public Range Range { get; set; }
    }

    public class Range
    {
        public Position Start { get; set; }
        public Position End { get; set; }
    }

    public class Position
    {
        public int Line { get; set; }
        public int Character { get; set; }
    }
}
