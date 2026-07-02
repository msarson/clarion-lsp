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

        public event Action<string, DiagnosticResult[]> DiagnosticsPublished;
        public event Action<WorkspaceApplyEdit> ApplyEditRequested;

        public ClarionLspService()
        {
            // Bridge the client's raw publishDiagnostics into the public typed event.
            _client.DiagnosticsReceived += (filePath, raw) =>
            {
                var handler = DiagnosticsPublished;
                if (handler == null) return;
                try { handler(filePath, ParseDiagnostics(raw)); }
                catch (Exception ex) { Log("DiagnosticsPublished handler error: " + ex.Message); }
            };

            // Bridge the client's raw workspace/applyEdit reverse-request into the public typed event.
            _client.ApplyEditReceived += (rawParams) =>
            {
                var handler = ApplyEditRequested;
                if (handler == null) return;
                try { handler(ParseWorkspaceApplyEdit(rawParams)); }
                catch (Exception ex) { Log("ApplyEditRequested handler error: " + ex.Message); }
            };
        }

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

        public Task<LocationResult[]> GetImplementationAsync(string filePath, int line, int character)
        {
            return Task.Run(() =>
            {
                Log($"GetImplementation {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.GetImplementation(filePath, line, character);
                var result = ParseLocations(raw);
                Log("GetImplementation result: " + result.Length + " location(s)");
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

        public Task<FoldingRange[]> GetFoldingRangesAsync(string filePath)
        {
            return Task.Run(() =>
            {
                Log("GetFoldingRanges " + Path.GetFileName(filePath));
                var raw = _client.GetFoldingRanges(filePath);
                var result = ParseFoldingRanges(raw);
                Log("GetFoldingRanges result: " + result.Length + " range(s)");
                return result;
            });
        }

        public Task<Range> PrepareRenameAsync(string filePath, int line, int character)
        {
            return Task.Run(() =>
            {
                Log($"PrepareRename {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.PrepareRename(filePath, line, character);
                var result = ParsePrepareRename(raw);
                Log("PrepareRename result: " + (result != null ? "range returned" : "null (not renameable)"));
                return result;
            });
        }

        public Task<RenameEdit[]> RenameAsync(string filePath, int line, int character, string newName)
        {
            return Task.Run(() =>
            {
                Log($"Rename {Path.GetFileName(filePath)} {line}:{character} newName={newName}");
                var raw = _client.Rename(filePath, line, character, newName);
                var result = ParseRenameEdits(raw);
                Log("Rename result: " + result.Length + " edit(s)");
                return result;
            });
        }

        public Task<CompletionResult[]> GetCompletionAsync(string filePath, int line, int character, string bufferText = null, int timeoutMs = 3000)
        {
            return Task.Run(() =>
            {
                Log($"GetCompletion {Path.GetFileName(filePath)} {line}:{character}" + (bufferText != null ? " (buffer)" : ""));
                if (!string.IsNullOrEmpty(bufferText)) _client.SyncBuffer(filePath, bufferText);
                var raw = _client.GetCompletion(filePath, line, character, timeoutMs);
                var result = ParseCompletion(raw);
                Log("GetCompletion result: " + result.Length + " item(s)");
                return result;
            });
        }

        public Task<SignatureHelpResult> GetSignatureHelpAsync(string filePath, int line, int character, string bufferText = null, int timeoutMs = 3000)
        {
            return Task.Run(() =>
            {
                Log($"GetSignatureHelp {Path.GetFileName(filePath)} {line}:{character}" + (bufferText != null ? " (buffer)" : ""));
                if (!string.IsNullOrEmpty(bufferText)) _client.SyncBuffer(filePath, bufferText);
                var raw = _client.GetSignatureHelp(filePath, line, character, timeoutMs);
                var result = ParseSignatureHelp(raw);
                Log("GetSignatureHelp result: " + (result != null ? result.Signatures.Length + " signature(s)" : "null"));
                return result;
            });
        }

        public Task<DocumentHighlight[]> GetDocumentHighlightsAsync(string filePath, int line, int character)
        {
            return Task.Run(() =>
            {
                Log($"GetDocumentHighlights {Path.GetFileName(filePath)} {line}:{character}");
                var raw = _client.GetDocumentHighlights(filePath, line, character);
                var result = ParseDocumentHighlights(raw);
                Log("GetDocumentHighlights result: " + result.Length + " highlight(s)");
                return result;
            });
        }

        public Task<TextEdit[]> FormatDocumentAsync(string filePath, int tabSize = 4, bool insertSpaces = false)
        {
            return Task.Run(() =>
            {
                Log($"FormatDocument {Path.GetFileName(filePath)} tabSize={tabSize} insertSpaces={insertSpaces}");
                var raw = _client.FormatDocument(filePath, tabSize, insertSpaces);
                var result = ParseTextEdits(raw);
                Log("FormatDocument result: " + result.Length + " edit(s)");
                return result;
            });
        }

        public Task<SelectionRange[]> GetSelectionRangesAsync(string filePath, Position[] positions)
        {
            return Task.Run(() =>
            {
                Log($"GetSelectionRanges {Path.GetFileName(filePath)} ({positions?.Length ?? 0} position(s))");
                var raws = new List<Dictionary<string, object>>();
                if (positions != null) foreach (var p in positions) raws.Add(PositionToRaw(p));
                var raw = _client.GetSelectionRanges(filePath, raws);
                var result = ParseSelectionRanges(raw);
                Log("GetSelectionRanges result: " + result.Length + " range(s)");
                return result;
            });
        }

        public Task<CodeActionResult[]> GetCodeActionsAsync(string filePath, Range range)
        {
            return Task.Run(() =>
            {
                if (range == null || range.Start == null || range.End == null)
                    return new CodeActionResult[0];
                Log($"GetCodeActions {Path.GetFileName(filePath)} {range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}");
                var raw = _client.GetCodeActions(filePath, range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
                var result = ParseCodeActions(raw);
                Log("GetCodeActions result: " + result.Length + " action(s)");
                return result;
            });
        }

        public Task<CodeLensResult[]> GetCodeLensesAsync(string filePath)
        {
            return Task.Run(() =>
            {
                Log("GetCodeLenses " + Path.GetFileName(filePath));
                var raw = _client.GetCodeLenses(filePath);
                var result = ParseCodeLenses(raw);
                Log("GetCodeLenses result: " + result.Length + " lens(es)");
                return result;
            });
        }

        public Task<CodeLensResult> ResolveCodeLensAsync(CodeLensResult lens)
        {
            return Task.Run(() =>
            {
                if (lens == null) return null;
                var rawLens = new Dictionary<string, object>();
                if (lens.Range != null) rawLens["range"] = RangeToRaw(lens.Range);
                if (lens.Data != null) rawLens["data"] = lens.Data;
                if (lens.Command != null) rawLens["command"] = CommandToRaw(lens.Command);
                var raw = _client.ResolveCodeLens(rawLens);
                var resolved = (raw != null && raw.ContainsKey("result"))
                    ? ParseCodeLens(raw["result"] as Dictionary<string, object>)
                    : null;
                return resolved ?? lens;
            });
        }

        public Task<bool> ExecuteCommandAsync(string command, object[] arguments)
        {
            return Task.Run(() =>
            {
                Log("ExecuteCommand " + command);
                var raw = _client.ExecuteCommand(command, arguments);
                bool ok = raw != null && !raw.ContainsKey("error");
                Log("ExecuteCommand result: " + (ok ? "ok" : "failed/timeout"));
                return ok;
            });
        }

        public Task<DiagnosticResult[]> GetDiagnosticsAsync(string filePath, string bufferText = null, int timeoutMs = 3000)
        {
            return Task.Run(() =>
            {
                Log("GetDiagnostics " + Path.GetFileName(filePath) + (bufferText != null ? " (buffer)" : ""));
                var raw = _client.GetDiagnostics(filePath, bufferText, timeoutMs);
                var result = ParseDiagnostics(raw);
                Log("GetDiagnostics result: " + result.Length + " diagnostic(s)");
                return result;
            });
        }

        public Task NotifyBufferChangedAsync(string filePath, string bufferText)
        {
            return Task.Run(() =>
            {
                Log("NotifyBufferChanged " + Path.GetFileName(filePath) + " (" + (bufferText?.Length ?? 0) + " chars)");
                _client.SyncBuffer(filePath, bufferText);
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

        private static Range ParsePrepareRename(Dictionary<string, object> raw)
        {
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return null;
                var result = raw["result"] as Dictionary<string, object>;
                if (result == null) return null;
                // Server returns { range: {...}, placeholder: "..." }
                var rangeObj = result.ContainsKey("range")
                    ? result["range"] as Dictionary<string, object>
                    : result;
                return ParseRange(rangeObj);
            }
            catch (Exception ex)
            {
                Log("ParsePrepareRename error: " + ex.Message);
                return null;
            }
        }

        private static RenameEdit[] ParseRenameEdits(Dictionary<string, object> raw)
        {
            var list = new List<RenameEdit>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();
                var result = raw["result"] as Dictionary<string, object>;
                if (result == null) return list.ToArray();

                // WorkspaceEdit.changes: { uri: TextEdit[] }
                if (result.ContainsKey("changes") && result["changes"] is Dictionary<string, object> changes)
                {
                    foreach (var kvp in changes)
                    {
                        string filePath = LspClient.UriToFilePath(kvp.Key);
                        var edits = kvp.Value as System.Collections.ArrayList;
                        if (edits == null) continue;
                        foreach (var edit in edits)
                        {
                            if (edit is Dictionary<string, object> te)
                            {
                                list.Add(new RenameEdit
                                {
                                    FilePath = filePath,
                                    Range = ParseRange(te.ContainsKey("range") ? te["range"] as Dictionary<string, object> : null),
                                    NewText = te.ContainsKey("newText") ? te["newText"]?.ToString() : null
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseRenameEdits error: " + ex.Message);
            }
            return list.ToArray();
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

        private static FoldingRange[] ParseFoldingRanges(Dictionary<string, object> raw)
        {
            var list = new List<FoldingRange>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();

                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();

                foreach (var item in items)
                {
                    if (item is Dictionary<string, object> fr)
                    {
                        int ToInt(string key)
                        {
                            if (fr.ContainsKey(key) && fr[key] != null)
                            {
                                try { return Convert.ToInt32(fr[key]); } catch { }
                            }
                            return 0;
                        }
                        list.Add(new FoldingRange
                        {
                            StartLine = ToInt("startLine"),
                            EndLine = ToInt("endLine"),
                            Kind = fr.ContainsKey("kind") ? fr["kind"]?.ToString() : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseFoldingRanges error: " + ex.Message);
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

        private static CompletionResult[] ParseCompletion(Dictionary<string, object> raw)
        {
            var list = new List<CompletionResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null)
                    return list.ToArray();

                // result is either CompletionItem[] or a CompletionList { items: [...] }.
                object result = raw["result"];
                var items = result as System.Collections.ArrayList;
                if (items == null)
                {
                    var asList = result as Dictionary<string, object>;
                    if (asList != null && asList.ContainsKey("items"))
                        items = asList["items"] as System.Collections.ArrayList;
                }
                if (items == null) return list.ToArray();

                foreach (var obj in items)
                {
                    if (!(obj is Dictionary<string, object> d)) continue;

                    string documentation = null;
                    if (d.ContainsKey("documentation"))
                    {
                        var docVal = d["documentation"];
                        documentation = docVal as string;
                        if (documentation == null && docVal is Dictionary<string, object> md && md.ContainsKey("value"))
                            documentation = md["value"]?.ToString();
                    }

                    var ci = new CompletionResult
                    {
                        Label = d.ContainsKey("label") ? d["label"]?.ToString() : null,
                        Kind = CompletionKindName(d.ContainsKey("kind") ? d["kind"] : null),
                        Detail = d.ContainsKey("detail") ? d["detail"]?.ToString() : null,
                        Documentation = documentation,
                        InsertText = d.ContainsKey("insertText") ? d["insertText"]?.ToString() : null
                    };
                    if (!string.IsNullOrEmpty(ci.Label)) list.Add(ci);
                }
            }
            catch (Exception ex)
            {
                Log("ParseCompletion error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static SignatureHelpResult ParseSignatureHelp(Dictionary<string, object> raw)
        {
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return null;
                var result = raw["result"] as Dictionary<string, object>;
                if (result == null) return null;

                var signatures = new List<SignatureInfo>();
                var sigList = result.ContainsKey("signatures") ? result["signatures"] as System.Collections.ArrayList : null;
                if (sigList != null)
                {
                    foreach (var so in sigList)
                    {
                        var sd = so as Dictionary<string, object>;
                        if (sd == null) continue;
                        string label = sd.ContainsKey("label") ? sd["label"]?.ToString() : null;

                        var parameters = new List<SignatureParameter>();
                        var paramList = sd.ContainsKey("parameters") ? sd["parameters"] as System.Collections.ArrayList : null;
                        if (paramList != null)
                        {
                            foreach (var po in paramList)
                            {
                                var pd = po as Dictionary<string, object>;
                                if (pd == null) continue;
                                parameters.Add(new SignatureParameter
                                {
                                    Label = ParseParamLabel(pd.ContainsKey("label") ? pd["label"] : null, label),
                                    Documentation = MarkupToString(pd.ContainsKey("documentation") ? pd["documentation"] : null)
                                });
                            }
                        }

                        signatures.Add(new SignatureInfo
                        {
                            Label = label,
                            Documentation = MarkupToString(sd.ContainsKey("documentation") ? sd["documentation"] : null),
                            Parameters = parameters.ToArray()
                        });
                    }
                }

                if (signatures.Count == 0) return null;

                return new SignatureHelpResult
                {
                    Signatures = signatures.ToArray(),
                    ActiveSignature = result.ContainsKey("activeSignature") ? SafeInt(result["activeSignature"]) : 0,
                    ActiveParameter = result.ContainsKey("activeParameter") ? SafeInt(result["activeParameter"]) : 0
                };
            }
            catch (Exception ex)
            {
                Log("ParseSignatureHelp error: " + ex.Message);
                return null;
            }
        }

        // An LSP parameter label is either a plain string or a [start, end] offset pair into the
        // owning signature's label — resolve the pair to the substring so consumers get real text.
        private static string ParseParamLabel(object label, string signatureLabel)
        {
            if (label is string s) return s;
            var arr = label as System.Collections.ArrayList;
            if (arr != null && arr.Count == 2 && signatureLabel != null)
            {
                try
                {
                    int start = SafeInt(arr[0]);
                    int end = SafeInt(arr[1]);
                    if (start >= 0 && end <= signatureLabel.Length && end > start)
                        return signatureLabel.Substring(start, end - start);
                }
                catch { }
            }
            return null;
        }

        private static DocumentHighlight[] ParseDocumentHighlights(Dictionary<string, object> raw)
        {
            var list = new List<DocumentHighlight>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return list.ToArray();
                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();
                foreach (var o in items)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    list.Add(new DocumentHighlight
                    {
                        Range = ParseRange(d.ContainsKey("range") ? d["range"] as Dictionary<string, object> : null),
                        Kind = HighlightKindName(d.ContainsKey("kind") ? d["kind"] : null)
                    });
                }
            }
            catch (Exception ex)
            {
                Log("ParseDocumentHighlights error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static string HighlightKindName(object kind)
        {
            if (kind == null) return "Text";
            switch (SafeInt(kind))
            {
                case 1: return "Text";
                case 2: return "Read";
                case 3: return "Write";
                default: return "Text";
            }
        }

        private static TextEdit[] ParseTextEdits(Dictionary<string, object> raw)
        {
            var list = new List<TextEdit>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return list.ToArray();
                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();
                foreach (var o in items)
                {
                    var te = o as Dictionary<string, object>;
                    if (te != null) list.Add(ParseTextEdit(te));
                }
            }
            catch (Exception ex)
            {
                Log("ParseTextEdits error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static TextEdit ParseTextEdit(Dictionary<string, object> te)
        {
            return new TextEdit
            {
                Range = ParseRange(te.ContainsKey("range") ? te["range"] as Dictionary<string, object> : null),
                NewText = te.ContainsKey("newText") ? te["newText"]?.ToString() : null
            };
        }

        private static SelectionRange[] ParseSelectionRanges(Dictionary<string, object> raw)
        {
            var list = new List<SelectionRange>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return list.ToArray();
                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();
                foreach (var o in items) list.Add(ParseSelectionRange(o as Dictionary<string, object>));
            }
            catch (Exception ex)
            {
                Log("ParseSelectionRanges error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static SelectionRange ParseSelectionRange(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new SelectionRange
            {
                Range = ParseRange(d.ContainsKey("range") ? d["range"] as Dictionary<string, object> : null),
                Parent = d.ContainsKey("parent") ? ParseSelectionRange(d["parent"] as Dictionary<string, object>) : null
            };
        }

        private static CodeActionResult[] ParseCodeActions(Dictionary<string, object> raw)
        {
            var list = new List<CodeActionResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return list.ToArray();
                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();
                foreach (var o in items)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;

                    var action = new CodeActionResult
                    {
                        Title = d.ContainsKey("title") ? d["title"]?.ToString() : null,
                        Kind = d.ContainsKey("kind") ? d["kind"]?.ToString() : null,
                        IsPreferred = d.ContainsKey("isPreferred") && d["isPreferred"] is bool && (bool)d["isPreferred"]
                    };

                    // A bare Command has command:string; a CodeAction nests a Command object (or none).
                    if (d.ContainsKey("command"))
                    {
                        if (d["command"] is string) action.Command = ParseCommand(d);
                        else action.Command = ParseCommand(d["command"] as Dictionary<string, object>);
                    }
                    if (d.ContainsKey("edit"))
                        action.Edit = ParseWorkspaceEditChanges(d["edit"] as Dictionary<string, object>);

                    list.Add(action);
                }
            }
            catch (Exception ex)
            {
                Log("ParseCodeActions error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static CommandInfo ParseCommand(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var args = new List<object>();
            var argList = d.ContainsKey("arguments") ? d["arguments"] as System.Collections.ArrayList : null;
            if (argList != null) foreach (var a in argList) args.Add(a);
            return new CommandInfo
            {
                Title = d.ContainsKey("title") ? d["title"]?.ToString() : null,
                Command = d.ContainsKey("command") ? d["command"]?.ToString() : null,
                Arguments = args.ToArray()
            };
        }

        private static WorkspaceEditChange[] ParseWorkspaceEditChanges(Dictionary<string, object> edit)
        {
            var list = new List<WorkspaceEditChange>();
            if (edit == null) return list.ToArray();
            try
            {
                // WorkspaceEdit.changes: { uri: TextEdit[] }
                if (edit.ContainsKey("changes") && edit["changes"] is Dictionary<string, object> changes)
                {
                    foreach (var kvp in changes)
                    {
                        var edits = new List<TextEdit>();
                        var arr = kvp.Value as System.Collections.ArrayList;
                        if (arr != null)
                            foreach (var e in arr) { var te = e as Dictionary<string, object>; if (te != null) edits.Add(ParseTextEdit(te)); }
                        list.Add(new WorkspaceEditChange { FilePath = LspClient.UriToFilePath(kvp.Key), Edits = edits.ToArray() });
                    }
                }
                // WorkspaceEdit.documentChanges: [ TextDocumentEdit { textDocument:{uri}, edits:[...] } ]
                else if (edit.ContainsKey("documentChanges") && edit["documentChanges"] is System.Collections.ArrayList docChanges)
                {
                    foreach (var dc in docChanges)
                    {
                        var dcd = dc as Dictionary<string, object>;
                        if (dcd == null) continue;
                        var td = dcd.ContainsKey("textDocument") ? dcd["textDocument"] as Dictionary<string, object> : null;
                        string uri = td != null && td.ContainsKey("uri") ? td["uri"]?.ToString() : null;
                        if (uri == null) continue;
                        var edits = new List<TextEdit>();
                        var arr = dcd.ContainsKey("edits") ? dcd["edits"] as System.Collections.ArrayList : null;
                        if (arr != null)
                            foreach (var e in arr) { var te = e as Dictionary<string, object>; if (te != null) edits.Add(ParseTextEdit(te)); }
                        list.Add(new WorkspaceEditChange { FilePath = LspClient.UriToFilePath(uri), Edits = edits.ToArray() });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ParseWorkspaceEditChanges error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static CodeLensResult[] ParseCodeLenses(Dictionary<string, object> raw)
        {
            var list = new List<CodeLensResult>();
            try
            {
                if (raw == null || !raw.ContainsKey("result") || raw["result"] == null) return list.ToArray();
                var items = raw["result"] as System.Collections.ArrayList;
                if (items == null) return list.ToArray();
                foreach (var o in items)
                {
                    var d = o as Dictionary<string, object>;
                    if (d != null) list.Add(ParseCodeLens(d));
                }
            }
            catch (Exception ex)
            {
                Log("ParseCodeLenses error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static CodeLensResult ParseCodeLens(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new CodeLensResult
            {
                Range = ParseRange(d.ContainsKey("range") ? d["range"] as Dictionary<string, object> : null),
                Command = d.ContainsKey("command") ? ParseCommand(d["command"] as Dictionary<string, object>) : null,
                Data = d.ContainsKey("data") ? d["data"] : null
            };
        }

        private static WorkspaceApplyEdit ParseWorkspaceApplyEdit(Dictionary<string, object> parms)
        {
            if (parms == null) return null;
            var edit = parms.ContainsKey("edit") ? parms["edit"] as Dictionary<string, object> : null;
            return new WorkspaceApplyEdit
            {
                Label = parms.ContainsKey("label") ? parms["label"]?.ToString() : null,
                Changes = ParseWorkspaceEditChanges(edit)
            };
        }

        private static string MarkupToString(object doc)
        {
            if (doc == null) return null;
            if (doc is string s) return s;
            var d = doc as Dictionary<string, object>;
            if (d != null && d.ContainsKey("value")) return d["value"]?.ToString();
            return null;
        }

        private static int SafeInt(object v)
        {
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static Dictionary<string, object> RangeToRaw(Range r)
        {
            if (r == null) return null;
            return new Dictionary<string, object>
            {
                { "start", PositionToRaw(r.Start) },
                { "end", PositionToRaw(r.End) }
            };
        }

        private static Dictionary<string, object> PositionToRaw(Position p)
        {
            return new Dictionary<string, object> { { "line", p?.Line ?? 0 }, { "character", p?.Character ?? 0 } };
        }

        private static Dictionary<string, object> CommandToRaw(CommandInfo c)
        {
            if (c == null) return null;
            return new Dictionary<string, object>
            {
                { "title", c.Title },
                { "command", c.Command },
                { "arguments", c.Arguments ?? new object[0] }
            };
        }

        private static DiagnosticResult[] ParseDiagnostics(List<Dictionary<string, object>> raw)
        {
            var list = new List<DiagnosticResult>();
            if (raw == null) return list.ToArray();
            try
            {
                foreach (var d in raw)
                {
                    if (d == null) continue;
                    list.Add(new DiagnosticResult
                    {
                        Severity = SeverityName(d.ContainsKey("severity") ? d["severity"] : null),
                        Message = d.ContainsKey("message") ? d["message"]?.ToString() : null,
                        Source = d.ContainsKey("source") ? d["source"]?.ToString() : null,
                        Range = ParseRange(d.ContainsKey("range") ? d["range"] as Dictionary<string, object> : null)
                    });
                }
            }
            catch (Exception ex)
            {
                Log("ParseDiagnostics error: " + ex.Message);
            }
            return list.ToArray();
        }

        private static string SeverityName(object severity)
        {
            if (severity == null) return "Error";
            switch (Convert.ToInt32(severity))
            {
                case 1: return "Error";
                case 2: return "Warning";
                case 3: return "Information";
                case 4: return "Hint";
                default: return "Error";
            }
        }

        private static string CompletionKindName(object kind)
        {
            if (kind == null) return "Text";
            // LSP CompletionItemKind: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#completionItemKind
            switch (Convert.ToInt32(kind))
            {
                case 1: return "Text";
                case 2: return "Method";
                case 3: return "Function";
                case 4: return "Constructor";
                case 5: return "Field";
                case 6: return "Variable";
                case 7: return "Class";
                case 8: return "Interface";
                case 9: return "Module";
                case 10: return "Property";
                case 11: return "Unit";
                case 12: return "Value";
                case 13: return "Enum";
                case 14: return "Keyword";
                case 15: return "Snippet";
                case 16: return "Color";
                case 17: return "File";
                case 18: return "Reference";
                case 19: return "Folder";
                case 20: return "EnumMember";
                case 21: return "Constant";
                case 22: return "Struct";
                case 23: return "Event";
                case 24: return "Operator";
                case 25: return "TypeParameter";
                default: return "Text";
            }
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
