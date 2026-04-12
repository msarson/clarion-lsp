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
        private readonly HashSet<string> _openDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        private void EnsureDocumentOpen(string filePath)
        {
            if (_openDocuments.Contains(filePath) || !File.Exists(filePath)) return;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string langId = (ext == ".inc" || ext == ".clw" || ext == ".equ") ? "clarion" : "plaintext";

            SendNotification("textDocument/didOpen", new Dictionary<string, object>
            {
                { "textDocument", new Dictionary<string, object>
                    {
                        { "uri", FilePathToUri(filePath) },
                        { "languageId", langId },
                        { "version", 1 },
                        { "text", File.ReadAllText(filePath) }
                    }
                }
            });
            _openDocuments.Add(filePath);
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

        public void Dispose() => Stop();
    }
}
