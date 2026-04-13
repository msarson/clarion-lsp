using System.Threading.Tasks;
using ClarionLsp.Contracts.Models;

namespace ClarionLsp.Contracts
{
    public interface IClarionLanguageClient
    {
        bool IsRunning { get; }

        Task<HoverResult> GetHoverAsync(string filePath, int line, int character, int timeoutMs = 3000);
        Task<LocationResult[]> GetDefinitionAsync(string filePath, int line, int character);
        Task<LocationResult[]> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true);
        Task<SymbolResult[]> GetDocumentSymbolsAsync(string filePath);
        Task<SymbolResult[]> FindWorkspaceSymbolAsync(string query);

        /// <summary>Returns the range of the symbol under the cursor, or null if it cannot be renamed.</summary>
        Task<Range> PrepareRenameAsync(string filePath, int line, int character);

        /// <summary>Renames the symbol under the cursor to <paramref name="newName"/> across the workspace.</summary>
        Task<RenameEdit[]> RenameAsync(string filePath, int line, int character, string newName);
    }
}
