using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ClarionLsp.Contracts;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;
using static ClarionLsp.Ods;

namespace ClarionLsp
{
    public class LspStartupCommand : AbstractCommand
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern void OutputDebugString(string message);

        private static ClarionLspService _service;
        private static readonly object _lock = new object();

        static LspStartupCommand()
        {
            OutputDebugString("[ClarionLsp] DLL loaded");
        }

        public override void Run()
        {
            Log("Startup Run() called");

            ProjectService.SolutionLoaded += OnSolutionLoaded;
            ProjectService.SolutionClosed += OnSolutionClosed;
            Log("Solution events hooked");

            // Start LSP immediately — with solution context if already open, standalone if not
            string existingSln = ProjectService.OpenSolution?.FileName;
            if (existingSln != null)
                Log("Solution already open at startup: " + existingSln);

            ThreadPool.QueueUserWorkItem(_ => StartLsp(existingSln));

            Log("Startup complete");
        }

        private static void OnSolutionLoaded(object sender, SolutionEventArgs e)
        {
            string slnPath = e.Solution?.FileName;
            Log("SolutionLoaded: " + slnPath);

            lock (_lock)
            {
                if (_service != null && _service.IsRunning)
                {
                    // Server already running — send updated paths with solution context
                    ThreadPool.QueueUserWorkItem(_ => UpdateLspPaths(slnPath));
                    return;
                }
            }

            // Server not yet running — start it with solution context
            ThreadPool.QueueUserWorkItem(_ => StartLsp(slnPath));
        }

        private static void OnSolutionClosed(object sender, EventArgs e)
        {
            Log("SolutionClosed — clearing project paths");
            ThreadPool.QueueUserWorkItem(_ => UpdateLspPaths(null));
        }

        private static void StartLsp(string slnPath)
        {
            lock (_lock)
            {
                if (_service != null && _service.IsRunning)
                {
                    Log("LSP already running — skipping start");
                    return;
                }

                string serverJs = ResolveLspServerPath();
                if (serverJs == null)
                {
                    Log("server.js not found — LSP unavailable");
                    Log("  Checked: {assemblyDir}\\lsp-server\\out\\server\\src\\server.js");
                    Log("  Checked: %USERPROFILE%\\.vscode\\extensions\\msarson.clarion-extensions-*\\out\\server\\src\\server.js");
                    Log("  Set 'Lsp.ServerPath' property to override");
                    return;
                }

                Log("server.js found: " + serverJs);

                string wsUri = null;
                string wsName = "Clarion";

                if (!string.IsNullOrEmpty(slnPath) && File.Exists(slnPath))
                {
                    string wsPath = Path.GetDirectoryName(slnPath);
                    wsName = Path.GetFileNameWithoutExtension(slnPath);
                    wsUri = "file:///" + wsPath.Replace("\\", "/").Replace(" ", "%20");
                    Log("Workspace: " + wsPath);
                }
                else
                {
                    Log("Starting LSP in standalone mode (no solution)");
                }

                var updatePaths = BuildUpdatePaths(slnPath);

                _service = new ClarionLspService();
                ClarionLspLocator.Current = null;

                bool ok = _service.Start(serverJs, wsUri, wsName, updatePaths);
                if (ok)
                {
                    ClarionLspLocator.Current = _service;
                    Log("LSP service registered — IClarionLanguageClient available");
                }
                else
                {
                    Log("LSP service failed to start");
                    _service.Dispose();
                    _service = null;
                }
            }
        }

        private static void UpdateLspPaths(string slnPath)
        {
            lock (_lock)
            {
                if (_service == null || !_service.IsRunning)
                {
                    Log("LSP not running — cannot update paths");
                    return;
                }

                string label = slnPath ?? "(no solution)";
                Log("Updating paths: " + label);
                var payload = BuildUpdatePaths(slnPath);
                _service.UpdatePaths(payload);
                Log("Paths updated");
            }
        }

        private static void StopLsp()
        {
            lock (_lock)
            {
                ClarionLspLocator.Current = null;
                if (_service != null)
                {
                    Log("Stopping service");
                    _service.Dispose();
                    _service = null;
                }
                Log("LSP service stopped");
            }
        }

        private static Dictionary<string, object> BuildUpdatePaths(string slnPath)
        {
            string wsPath = string.IsNullOrEmpty(slnPath) ? null : Path.GetDirectoryName(slnPath);
            var versionConfig = ClarionVersionService.Detect();

            var libSrcPaths = new List<object>();
            if (versionConfig?.LibSrcPaths != null)
                foreach (var p in versionConfig.LibSrcPaths)
                    libSrcPaths.Add(p);

            var lookupExtensions = new List<object> { ".clw", ".inc", ".equ", ".eq" };

            string configuration = "";
            if (wsPath != null)
            {
                try
                {
                    string activeConfig = ProjectService.OpenSolution?.Preferences?.ActiveConfiguration;
                    if (!string.IsNullOrEmpty(activeConfig))
                        configuration = activeConfig;
                }
                catch { }
            }

            var macros = new Dictionary<string, object>();
            if (versionConfig?.Macros != null)
                foreach (var kv in versionConfig.Macros)
                    macros[kv.Key] = kv.Value;

            var projectPaths = wsPath != null ? new List<object> { wsPath } : new List<object>();

            var payload = new Dictionary<string, object>
            {
                { "projectPaths", projectPaths },
                { "solutionFilePath", slnPath ?? "" },
                { "libsrcPaths", libSrcPaths },
                { "lookupExtensions", lookupExtensions },
                { "macros", macros }
            };

            if (versionConfig != null)
            {
                payload["clarionVersion"] = versionConfig.Name;
                payload["configuration"] = configuration;

                if (!string.IsNullOrEmpty(versionConfig.BinPath) && !string.IsNullOrEmpty(versionConfig.RedFileName))
                {
                    string redFile = Path.Combine(versionConfig.BinPath, versionConfig.RedFileName);
                    if (File.Exists(redFile))
                    {
                        payload["redirectionFile"] = versionConfig.RedFileName;
                        payload["redirectionPaths"] = new List<object> { versionConfig.BinPath };
                        Log("clarion/updatePaths — redirectionFile=" + versionConfig.RedFileName);
                        Log("clarion/updatePaths — redirectionPaths=[" + versionConfig.BinPath + "]");
                    }
                    else
                    {
                        Log("clarion/updatePaths — redirectionFile not found: " + redFile);
                    }
                }
            }

            if (wsPath != null)
                Log("clarion/updatePaths — projectPaths=[" + wsPath + "]");
            else
                Log("clarion/updatePaths — projectPaths=[] (standalone)");

            Log("clarion/updatePaths — solutionFilePath=" + (slnPath ?? ""));
            Log("clarion/updatePaths — clarionVersion=" + (versionConfig?.Name ?? "(none)"));
            Log("clarion/updatePaths — configuration=" + configuration);
            Log("clarion/updatePaths — libsrcPaths count=" + libSrcPaths.Count);
            Log("clarion/updatePaths — lookupExtensions=" + string.Join(",", lookupExtensions));

            return payload;
        }

        private static string ResolveLspServerPath()
        {
            // 1. User-configured setting
            try
            {
                string configured = PropertyService.Get("Lsp.ServerPath", "");
                if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                {
                    Log("Using configured Lsp.ServerPath: " + configured);
                    return configured;
                }
            }
            catch { }

            // 2. Relative to addin assembly
            string assemblyDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string lspPath = Path.Combine(assemblyDir, "lsp-server", "out", "server", "src", "server.js");
            if (File.Exists(lspPath))
            {
                Log("Using bundled server.js: " + lspPath);
                return lspPath;
            }

            // 3. Auto-discover from VS Code extensions
            try
            {
                string extDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".vscode", "extensions");

                if (Directory.Exists(extDir))
                {
                    string best = null;
                    Version bestVer = null;

                    foreach (string dir in Directory.GetDirectories(extDir, "msarson.clarion-extensions-*"))
                    {
                        string candidate = Path.Combine(dir, "out", "server", "src", "server.js");
                        if (!File.Exists(candidate)) continue;

                        // Parse version from folder name e.g. msarson.clarion-extensions-0.8.7
                        string folderName = Path.GetFileName(dir);
                        string verStr = folderName.Replace("msarson.clarion-extensions-", "");
                        Version ver;
                        if (Version.TryParse(verStr, out ver) && (bestVer == null || ver > bestVer))
                        {
                            bestVer = ver;
                            best = candidate;
                        }
                    }

                    if (best != null)
                    {
                        Log("Auto-discovered server.js v" + bestVer + ": " + best);
                        return best;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Auto-discover failed: " + ex.Message);
            }

            return null;
        }
    }
}
