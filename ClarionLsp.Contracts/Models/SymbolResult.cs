using System.Collections.Generic;

namespace ClarionLsp.Contracts.Models
{
    public class SymbolResult
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string FilePath { get; set; }
        public Range Range { get; set; }
        public string ContainerName { get; set; }

        /// <summary>DocumentSymbol.detail — e.g. a control's USE(...) or a method's signature. Optional.</summary>
        public string Detail { get; set; }

        /// <summary>Nested DocumentSymbol children (windows/controls, class members, routine data, …).
        /// Null/empty for a flat SymbolInformation-style result. Enables a hierarchical outline.</summary>
        public List<SymbolResult> Children { get; set; }
    }
}
