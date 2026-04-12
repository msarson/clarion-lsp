using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClarionLsp.Contracts;
using ClarionLsp.Contracts.Models;
using static ClarionLsp.Ods;

namespace ClarionLsp
{
    internal class ClarionLspService : IClarionLanguageClient, IDisposable
    {
        private readonly LspClient _client = new LspClient();

        public bool IsRunning => _client.IsRunning;

        internal bool Start(string serverJsPath, string workspaceUri, string workspaceName,
            Dictionary<string, object> updatePaths)
        {
            Log("Starting service — server=" + serverJsPath);
            _client.SetUpdatePaths(updatePaths);
            bool ok = _client.Start(serverJsPath, workspaceUri, workspaceName);
            Log("Service start result: " + ok);
            return ok;
        }

        internal void Stop()
        {
            Log("Stopping service");
            _client.Stop();
        }

        internal void UpdatePaths(Dictionary<string, object> payload)
        {
            _client.SendUpdatePaths(payload);
        }

        // ── IClarionLanguageClient ──────────────────────────────────────────

        public Task<HoverResult> GetHoverAsync(string filePath, int line, int character, int timeoutMs = 3000)
        {
            return Task.Run(() =>
            {
                Log($"GetHover {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.GetHover(filePath, line, character, timeoutMs);
                var result = ParseHover(raw);
                Log("GetHover result: " + (result != null ? result.Contents?.Substring(0, Math.Min(80, result.Contents?.Length ?? 0)) : "null"));
                return result;
            });
        }

        public Task<LocationResult[]> GetDefinitionAsync(string filePath, int line, int character)
        {
            return Task.Run(() =>
            {
                Log($"GetDefinition {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.GetDefinition(filePath, line, character);
                var result = ParseLocations(raw);
                Log("GetDefinition result: " + result.Length + " location(s)");
                return result;
            });
        }

        public Task<LocationResult[]> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true)
        {
            return Task.Run(() =>
            {
                Log($"GetReferences {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.GetReferences(filePath, line, character, includeDeclaration);
                var result = ParseLocations(raw);
                Log("GetReferences result: " + result.Length + " location(s)");
                return result;
            });
        }

        public Task<SymbolResult[]> GetDocumentSymbolsAsync(string filePath)
        {
            return Task.Run(() =>
            {
                Log("GetDocumentSymbols " + Path.GetFileName(filePath));
                var raw = _client.GetDocumentSymbols(filePath);
                var result = ParseDocumentSymbols(raw, filePath);
                Log("GetDocumentSymbols result: " + result.Length + " symbol(s)");
                return result;
            });
        }

        public Task<SymbolResult[]> FindWorkspaceSymbolAsync(string query)
        {
            return Task.Run(() =>
            {
                Log("FindWorkspaceSymbol query=" + query);
                var raw = _client.FindWorkspaceSymbol(query);
                var result = ParseWorkspaceSymbols(raw);
                Log("FindWorkspaceSymbol result: " + result.Length + " symbol(s)");
                return result;
            });
        }

        // ── Response parsers ───────────────────────────────────────────────

        private static HoverResult ParseHover(Dictionary<string, object> raw)
        {
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return null;

                var result = raw["result"] as Dictionary<string, object>;
                if (result == null) return null;

                string contents = null;
                if (result.ContainsKey("contents"))
                {
                    var c = result["contents"];
                    if (c is string s)
                        contents = s;
                    else if (c is Dictionary<string, object> cd && cd.ContainsKey("value"))
                        contents = cd["value"]?.ToString();
                }

                if (string.IsNullOrEmpty(contents)) return null;

                return new HoverResult
                {
                    Contents = contents,
                    Range = ParseRange(result.ContainsKey("range") ? result["range"] as Dictionary<string, object> : null)
                };
            }
            catch (Exception ex)
            {
                Log("ParseHover error: " + ex.Message);
                return null;
            }
        }

        private static LocationResult[] ParseLocations(Dictionary<string, object> raw)
        {
            var list = new List<LocationResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();

                // Result can be a single location or an array
                var resultObj = raw["result"];
                var items = resultObj as System.Collections.ArrayList
                    ?? (resultObj is Dictionary<string, object> single
                        ? new System.Collections.ArrayList { single }
                        : null);

                if (items == null) return list.ToArray();

                foreach (var item in items)
                {
                    if (item is Dictionary<string, object> loc)
                    {
                        string uri = loc.ContainsKey("uri") ? loc["uri"]?.ToString() : null;
                        if (string.IsNullOrEmpty(uri)) continue;
                        list.Add(new LocationResult
                        {
                            FilePath = LspClient.UriToFilePath(uri),
                            Range = ParseRange(loc.ContainsKey("range") ? loc["range"] as Dictionary<string, object> : null)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseLocations error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static SymbolResult[] ParseDocumentSymbols(Dictionary<string, object> raw, string filePath)
        {
            var list = new List<SymbolResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();

                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();

                foreach (var item in items)
                {
                    if (item is Dictionary<string, object> sym)
                    {
                        list.Add(new SymbolResult
                        {
                            Name = sym.ContainsKey("name") ? sym["name"]?.ToString() : null,
                            Kind = SymbolKindName(sym.ContainsKey("kind") ? sym["kind"] : null),
                            FilePath = filePath,
                            Range = ParseRange(sym.ContainsKey("range") ? sym["range"] as Dictionary<string, object> : null),
                            ContainerName = sym.ContainsKey("containerName") ? sym["containerName"]?.ToString() : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseDocumentSymbols error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static SymbolResult[] ParseWorkspaceSymbols(Dictionary<string, object> raw)
        {
            var list = new List<SymbolResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();

                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();

                foreach (var item in items)
                {
                    if (item is Dictionary<string, object> sym)
                    {
                        string filePath = null;
                        Range range = null;
                        if (sym.ContainsKey("location") && sym["location"] is Dictionary<string, object> loc)
                        {
                            string uri = loc.ContainsKey("uri") ? loc["uri"]?.ToString() : null;
                            if (uri != null) filePath = LspClient.UriToFilePath(uri);
                            range = ParseRange(loc.ContainsKey("range") ? loc["range"] as Dictionary<string, object> : null);
                        }

                        list.Add(new SymbolResult
                        {
                            Name = sym.ContainsKey("name") ? sym["name"]?.ToString() : null,
                            Kind = SymbolKindName(sym.ContainsKey("kind") ? sym["kind"] : null),
                            FilePath = filePath,
                            Range = range,
                            ContainerName = sym.ContainsKey("containerName") ? sym["containerName"]?.ToString() : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseWorkspaceSymbols error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static Range ParseRange(Dictionary<string, object> raw)
        {
            if (raw == null) return null;
            return new Range
            {
                Start = ParsePosition(raw.ContainsKey("start") ? raw["start"] as Dictionary<string, object> : null),
                End = ParsePosition(raw.ContainsKey("end") ? raw["end"] as Dictionary<string, object> : null)
            };
        }

        private static Position ParsePosition(Dictionary<string, object> raw)
        {
            if (raw == null) return null;
            return new Position
            {
                Line = raw.ContainsKey("line") ? Convert.ToInt32(raw["line"]) : 0,
                Character = raw.ContainsKey("character") ? Convert.ToInt32(raw["character"]) : 0
            };
        }

        private static string SymbolKindName(object kind)
        {
            if (kind == null) return "Unknown";
            // LSP SymbolKind values: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#symbolKind
            switch (Convert.ToInt32(kind))
            {
                case 1: return "File";
                case 2: return "Module";
                case 3: return "Namespace";
                case 4: return "Package";
                case 5: return "Class";
                case 6: return "Method";
                case 7: return "Property";
                case 8: return "Field";
                case 9: return "Constructor";
                case 10: return "Enum";
                case 11: return "Interface";
                case 12: return "Function";
                case 13: return "Variable";
                case 14: return "Constant";
                case 15: return "String";
                default: return "Symbol";
            }
        }

        public void Dispose() => Stop();
    }
}
