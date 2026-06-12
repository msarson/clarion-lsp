using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using static ClarionLsp.Ods;

namespace ClarionLsp
{
    internal class LspClient : IDisposable
    {
        private Process _process;
        private readonly object _writeLock = new object();
        private int _nextId = 1;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly Dictionary<int, string> _responses = new Dictionary<int, string>();
        private readonly AutoResetEvent _responseReceived = new AutoResetEvent(false);
        private Thread _readerThread;
        private volatile bool _running;
        private Dictionary<string, object> _pendingUpdatePaths;

        // Open documents tracked by URI-able file path → last sent version (didOpen=1, then ++ per didChange).
        private readonly Dictionary<string, int> _openDocuments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Content key of the last text synced per document, used to skip needless re-tokenizes.
        private readonly Dictionary<string, long> _lastSyncedHash = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly object _docSyncLock = new object();

        // Diagnostics cache, keyed by canonical URI, populated by textDocument/publishDiagnostics.
        private const int MaxCachedDiagnosticFiles = 50;
        private readonly Dictionary<string, DiagnosticSet> _diagnostics =
            new Dictionary<string, DiagnosticSet>(StringComparer.OrdinalIgnoreCase);
        private readonly object _diagnosticsLock = new object();

        /// <summary>Raised when the server publishes diagnostics: (filePath, raw diagnostic dicts).</summary>
        public event Action<string, List<Dictionary<string, object>>> DiagnosticsReceived;

        public bool IsRunning => _running && _process != null && !_process.HasExited;

        public void SetUpdatePaths(Dictionary<string, object> updatePaths)
        {
            _pendingUpdatePaths = updatePaths;
        }

        public bool Start(string serverJsPath, string workspaceUri, string workspaceName)
        {
            if (_running) return true;
            if (!File.Exists(serverJsPath)) return false;

            try
            {
                string lspDir = Path.GetDirectoryName(serverJsPath);
                string lspRoot = Path.GetFullPath(Path.Combine(lspDir, "..", "..", ".."));
                string nodeExe = Path.Combine(lspRoot, "node.exe");
                if (!File.Exists(nodeExe))
                    nodeExe = "node";

                Log("Starting: " + nodeExe + " \"" + serverJsPath + "\" --stdio");

                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = nodeExe,
                        Arguments = "\"" + serverJsPath + "\" --stdio",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                _process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log("stderr: " + e.Data);
                };

                _process.Start();
                _process.BeginErrorReadLine();
                _running = true;

                Log("Process started PID=" + _process.Id);

                _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "ClarionLsp-Reader" };
                _readerThread.Start();

                var initParams = new Dictionary<string, object>
                {
                    { "processId", Process.GetCurrentProcess().Id },
                    { "capabilities", new Dictionary<string, object>() },
                    { "rootUri", (object)workspaceUri ?? null },
                };

                if (!string.IsNullOrEmpty(workspaceUri))
                {
                    initParams["workspaceFolders"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "uri", workspaceUri },
                            { "name", workspaceName ?? "Clarion" }
                        }
                    };
                }
                else
                {
                    initParams["workspaceFolders"] = new object[0];
                }

                Log("Sending initialize...");
                var initResult = SendRequest("initialize", initParams, 15000);
                if (initResult == null)
                {
                    Log("Initialize timed out");
                    Stop();
                    return false;
                }

                Log("Initialize OK");
                SendNotification("initialized", new Dictionary<string, object>());

                if (_pendingUpdatePaths != null)
                {
                    Log("Sending clarion/updatePaths...");
                    SendNotification("clarion/updatePaths", _pendingUpdatePaths);
                    _pendingUpdatePaths = null;
                }

                Thread.Sleep(1000);
                Log("Ready");
                return true;
            }
            catch (Exception ex)
            {
                Log("Start failed: " + ex.Message);
                Stop();
                return false;
            }
        }

        public void SendUpdatePaths(Dictionary<string, object> payload)
        {
            Log("Sending clarion/updatePaths...");
            SendNotification("clarion/updatePaths", payload);
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    SendNotification("shutdown", null);
                    Thread.Sleep(200);
                    SendNotification("exit", null);
                    Thread.Sleep(200);
                    if (!_process.HasExited) _process.Kill();
                }
            }
            catch { }
            _process = null;
        }

        public Dictionary<string, object> GetHover(string filePath, int line, int character, int timeoutMs = 3000)
        {
            EnsureDocumentOpen(filePath);
            return SendRequest("textDocument/hover", BuildTextDocumentPosition(filePath, line, character), timeoutMs);
        }

        public Dictionary<string, object> GetDefinition(string filePath, int line, int character)
        {
            return SendTextDocumentPositionRequest("textDocument/definition", filePath, line, character);
        }

        public Dictionary<string, object> GetReferences(string filePath, int line, int character, bool includeDeclaration = true)
        {
            EnsureDocumentOpen(filePath);
            var parms = BuildTextDocumentPosition(filePath, line, character);
            parms["context"] = new Dictionary<string, object> { { "includeDeclaration", includeDeclaration } };
            return SendRequest("textDocument/references", parms);
        }

        public Dictionary<string, object> GetDocumentSymbols(string filePath)
        {
            EnsureDocumentOpen(filePath);
            var parms = new Dictionary<string, object>
            {
                { "textDocument", new Dictionary<string, object> { { "uri", FilePathToUri(filePath) } } }
            };
            return SendRequest("textDocument/documentSymbol", parms);
        }

        public Dictionary<string, object> FindWorkspaceSymbol(string query)
        {
            return SendRequest("workspace/symbol", new Dictionary<string, object> { { "query", query } });
        }

        public Dictionary<string, object> PrepareRename(string filePath, int line, int character)
        {
            return SendTextDocumentPositionRequest("textDocument/prepareRename", filePath, line, character);
        }

        public Dictionary<string, object> Rename(string filePath, int line, int character, string newName)
        {
            EnsureDocumentOpen(filePath);
            var parms = BuildTextDocumentPosition(filePath, line, character);
            parms["newName"] = newName;
            return SendRequest("textDocument/rename", parms);
        }

        public Dictionary<string, object> GetCompletion(string filePath, int line, int character, int timeoutMs = 3000)
        {
            EnsureDocumentOpen(filePath);
            return SendRequest("textDocument/completion", BuildTextDocumentPosition(filePath, line, character), timeoutMs);
        }

        /// <summary>
        /// Trigger a fresh analysis and return the raw diagnostics the server publishes for the
        /// file, waiting up to <paramref name="timeoutMs"/>. Pass <paramref name="bufferText"/> to
        /// analyze live unsaved content; otherwise the on-disk file is (re)analyzed. On timeout the
        /// last-known diagnostics for the file are returned (empty if none have ever arrived).
        /// </summary>
        public List<Dictionary<string, object>> GetDiagnostics(string filePath, string bufferText = null, int timeoutMs = 3000)
        {
            if (!IsRunning || string.IsNullOrEmpty(filePath)) return new List<Dictionary<string, object>>();

            string key = CanonicalizeUri(FilePathToUri(filePath));
            DiagnosticSet set;
            lock (_diagnosticsLock)
            {
                if (!_diagnostics.TryGetValue(key, out set))
                {
                    set = new DiagnosticSet();
                    _diagnostics[key] = set;
                    EvictOldestIfFull_NoLock();
                }
                // Arm the wait BEFORE triggering, so a publish can't slip in between.
                set.Ready.Reset();
            }

            bool triggered;
            if (!string.IsNullOrEmpty(bufferText)) triggered = SyncBuffer(filePath, bufferText);
            else if (IsDocumentOpen(filePath)) triggered = SyncFromDisk(filePath);
            else triggered = EnsureDocumentOpen(filePath);

            // Only wait for a fresh publish if we actually changed something on the server.
            // If nothing changed, no new diagnostics are coming — return the last-known set now
            // instead of blocking for the full timeout on every unchanged-buffer poll.
            if (triggered) set.Ready.Wait(timeoutMs);

            lock (_diagnosticsLock)
                return set.Raw != null
                    ? new List<Dictionary<string, object>>(set.Raw)
                    : new List<Dictionary<string, object>>();
        }

        /// <summary>Thread-safe check of whether a document has been opened on the server.</summary>
        private bool IsDocumentOpen(string filePath)
        {
            lock (_docSyncLock) return _openDocuments.ContainsKey(filePath);
        }

        /// <summary>
        /// Open the on-disk file on the server if not already open. Returns true if a didOpen
        /// was actually sent (so a fresh publishDiagnostics is expected), false if it was already
        /// open or the file is missing.
        /// </summary>
        private bool EnsureDocumentOpen(string filePath)
        {
            lock (_docSyncLock)
            {
                if (_openDocuments.ContainsKey(filePath) || !File.Exists(filePath)) return false;

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string langId = (ext == ".inc" || ext == ".clw" || ext == ".equ") ? "clarion" : "plaintext";

                string text = File.ReadAllText(filePath);
                SendNotification("textDocument/didOpen", new Dictionary<string, object>
                {
                    { "textDocument", new Dictionary<string, object>
                        {
                            { "uri", FilePathToUri(filePath) },
                            { "languageId", langId },
                            { "version", 1 },
                            { "text", text }
                        }
                    }
                });
                _openDocuments[filePath] = 1;
                _lastSyncedHash[filePath] = ContentKey(text);
                return true;
            }
        }

        /// <summary>
        /// Sync an in-memory buffer (e.g. a live editor with unsaved generated source) to the
        /// server: didOpen on first sight, otherwise a full-document didChange with an
        /// incremented version. No-op if the text is unchanged since the last sync, so it is
        /// cheap to call on an editor change/idle timer. Triggers re-validation (and a
        /// subsequent textDocument/publishDiagnostics) on the server. Returns true if a
        /// didOpen/didChange was actually sent (so a fresh publish is expected), false if the
        /// text was unchanged since the last sync (no-op).
        /// </summary>
        public bool SyncBuffer(string filePath, string text)
        {
            if (!IsRunning || string.IsNullOrEmpty(filePath) || text == null) return false;

            lock (_docSyncLock)
            {
                string uri = FilePathToUri(filePath);
                long key = ContentKey(text);

                int currentVersion;
                if (_openDocuments.TryGetValue(filePath, out currentVersion))
                {
                    long lastKey;
                    if (_lastSyncedHash.TryGetValue(filePath, out lastKey) && lastKey == key)
                        return false; // unchanged — avoid a needless re-tokenize

                    int nextVersion = currentVersion + 1;
                    SendNotification("textDocument/didChange", new Dictionary<string, object>
                    {
                        { "textDocument", new Dictionary<string, object> { { "uri", uri }, { "version", nextVersion } } },
                        { "contentChanges", new object[] { new Dictionary<string, object> { { "text", text } } } }
                    });
                    _openDocuments[filePath] = nextVersion;
                    _lastSyncedHash[filePath] = key;
                    return true;
                }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string langId = (ext == ".inc" || ext == ".clw" || ext == ".equ") ? "clarion" : "plaintext";
                SendNotification("textDocument/didOpen", new Dictionary<string, object>
                {
                    { "textDocument", new Dictionary<string, object>
                        {
                            { "uri", uri }, { "languageId", langId }, { "version", 1 }, { "text", text }
                        }
                    }
                });
                _openDocuments[filePath] = 1;
                _lastSyncedHash[filePath] = key;
                return true;
            }
        }

        // 64-bit content key: high 32 bits = length, low 32 bits = string hash. Folding length in
        // means two buffers of different length can never collide, so a real edit is only ever
        // missed on a genuine 32-bit hash collision between two same-length buffers.
        private static long ContentKey(string text) => ((long)text.Length << 32) | (uint)text.GetHashCode();

        /// <summary>
        /// Force the server to re-analyze an already-open file using its current on-disk
        /// contents (full-document didChange). Falls through to didOpen if not yet open.
        /// Returns true if an update was actually sent (disk content differed from last sync).
        /// </summary>
        private bool SyncFromDisk(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try { return SyncBuffer(filePath, File.ReadAllText(filePath)); }
            catch { return false; }
        }

        private Dictionary<string, object> SendTextDocumentPositionRequest(string method, string filePath, int line, int character)
        {
            EnsureDocumentOpen(filePath);
            return SendRequest(method, BuildTextDocumentPosition(filePath, line, character));
        }

        private Dictionary<string, object> BuildTextDocumentPosition(string filePath, int line, int character)
        {
            return new Dictionary<string, object>
            {
                { "textDocument", new Dictionary<string, object> { { "uri", FilePathToUri(filePath) } } },
                { "position", new Dictionary<string, object> { { "line", line }, { "character", character } } }
            };
        }

        internal Dictionary<string, object> SendRequest(string method, Dictionary<string, object> parms, int timeoutMs = 5000)
        {
            if (!_running || _process == null || _process.HasExited) return null;

            int id = Interlocked.Increment(ref _nextId);
            var request = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" }, { "id", id }, { "method", method }
            };
            if (parms != null) request["params"] = parms;

            WriteMessage(_serializer.Serialize(request));

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                lock (_responses)
                {
                    string response;
                    if (_responses.TryGetValue(id, out response))
                    {
                        _responses.Remove(id);
                        return _serializer.Deserialize<Dictionary<string, object>>(response);
                    }
                }
                _responseReceived.WaitOne(100);
            }
            return null;
        }

        internal void SendNotification(string method, Dictionary<string, object> parms)
        {
            if (!_running || _process == null || _process.HasExited) return;
            var msg = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "method", method } };
            if (parms != null) msg["params"] = parms;
            WriteMessage(_serializer.Serialize(msg));
        }

        private void WriteMessage(string json)
        {
            lock (_writeLock)
            {
                try
                {
                    byte[] content = Encoding.UTF8.GetBytes(json);
                    byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + content.Length + "\r\n\r\n");
                    _process.StandardInput.BaseStream.Write(header, 0, header.Length);
                    _process.StandardInput.BaseStream.Write(content, 0, content.Length);
                    _process.StandardInput.BaseStream.Flush();
                }
                catch { }
            }
        }

        private void ReadLoop()
        {
            try
            {
                var stream = _process.StandardOutput.BaseStream;
                while (_running && !_process.HasExited)
                {
                    string json = ReadMessage(stream);
                    if (json == null) break;
                    try
                    {
                        var msg = _serializer.Deserialize<Dictionary<string, object>>(json);
                        if (msg.ContainsKey("id") && msg["id"] != null)
                        {
                            int id;
                            if (int.TryParse(msg["id"].ToString(), out id))
                            {
                                lock (_responses) { _responses[id] = json; }
                                _responseReceived.Set();
                            }
                        }
                        else if (msg.ContainsKey("method"))
                        {
                            // Server-initiated notification (no id).
                            var method = msg["method"] as string;
                            if (method == "textDocument/publishDiagnostics")
                                HandlePublishDiagnostics(msg.ContainsKey("params") ? msg["params"] as Dictionary<string, object> : null);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private string ReadMessage(Stream stream)
        {
            int contentLength = -1;
            var header = new StringBuilder();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return null;
                header.Append((char)b);
                if (header.ToString().EndsWith("\r\n\r\n"))
                {
                    foreach (string line in header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            int val;
                            if (int.TryParse(line.Substring(15).Trim(), out val))
                                contentLength = val;
                        }
                    }
                    break;
                }
            }
            if (contentLength <= 0) return null;

            byte[] buffer = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = stream.Read(buffer, read, contentLength - read);
                if (n <= 0) return null;
                read += n;
            }
            return Encoding.UTF8.GetString(buffer);
        }

        public static string FilePathToUri(string filePath)
            => "file:///" + filePath.Replace("\\", "/").Replace(" ", "%20");

        public static string UriToFilePath(string uri)
        {
            if (uri != null && uri.StartsWith("file:///"))
                return uri.Substring(8).Replace("/", "\\").Replace("%20", " ");
            return uri ?? string.Empty;
        }

        // Round-trip a server-supplied URI through our own formatting so cache keys are
        // consistent regardless of casing/encoding differences from what we sent.
        private static string CanonicalizeUri(string uri) => FilePathToUri(UriToFilePath(uri));

        private void HandlePublishDiagnostics(Dictionary<string, object> parms)
        {
            if (parms == null) return;
            string uri = parms.ContainsKey("uri") ? parms["uri"] as string : null;
            if (string.IsNullOrEmpty(uri)) return;

            string canonical = CanonicalizeUri(uri);
            string filePath = UriToFilePath(uri);

            var raw = new List<Dictionary<string, object>>();
            var diagList = parms.ContainsKey("diagnostics") ? parms["diagnostics"] as System.Collections.ArrayList : null;
            if (diagList != null)
            {
                foreach (var obj in diagList)
                {
                    var d = obj as Dictionary<string, object>;
                    if (d != null) raw.Add(d);
                }
            }

            lock (_diagnosticsLock)
            {
                DiagnosticSet set;
                if (!_diagnostics.TryGetValue(canonical, out set))
                {
                    set = new DiagnosticSet();
                    _diagnostics[canonical] = set;
                    EvictOldestIfFull_NoLock();
                }
                set.Raw = raw;
                set.LastUpdateTicks = DateTime.UtcNow.Ticks;
                set.Ready.Set();
            }

            try { DiagnosticsReceived?.Invoke(filePath, raw); } catch { }
            Log("publishDiagnostics: " + raw.Count + " entry(ies) for " + Path.GetFileName(filePath));
        }

        private void EvictOldestIfFull_NoLock()
        {
            if (_diagnostics.Count <= MaxCachedDiagnosticFiles) return;
            string oldestKey = null;
            long oldest = long.MaxValue;
            foreach (var kv in _diagnostics)
            {
                if (kv.Value.LastUpdateTicks < oldest) { oldest = kv.Value.LastUpdateTicks; oldestKey = kv.Key; }
            }
            if (oldestKey != null) _diagnostics.Remove(oldestKey);
        }

        private class DiagnosticSet
        {
            public List<Dictionary<string, object>> Raw;
            public long LastUpdateTicks;
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
        }

        public void Dispose() => Stop();
    }
}
