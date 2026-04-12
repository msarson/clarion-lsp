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
    }
}
