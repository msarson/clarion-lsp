using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static ClarionLsp.Ods;

namespace ClarionLsp
{
    /// <summary>
    /// Data resolved from the IDE's live Versions/ClarionVersion objects via reflection.
    /// All fields come directly from the IDE — no XML parsing, no path guessing.
    /// </summary>
    internal class ClarionVersionConfig
    {
        public string Name { get; set; }
        public string BinPath { get; set; }
        public string RedFileName { get; set; }
        public Dictionary<string, string> Macros { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> LibSrcPaths { get; set; } = new List<string>();
    }

    internal static class ClarionVersionService
    {
        /// <summary>
        /// Calls Versions.SetActiveVersionFromSolution() then Versions.GetVersion(true)
        /// via reflection on Clarion.Core.dll and returns the resolved config.
        /// </summary>
        public static ClarionVersionConfig Detect()
        {
            try
            {
                var versionsType = GetVersionsType();
                if (versionsType == null)
                {
                    Log("Clarion.Core.Options.Versions type not found");
                    return null;
                }

                // Ensure the solution-specific version is active before querying
                try
                {
                    versionsType.InvokeMember("SetActiveVersionFromSolution",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                        null, null, null);
                }
                catch { /* non-fatal */ }

                // GetVersion(bool forWin) — pass true for Windows Clarion
                var clarionVersion = versionsType.InvokeMember("GetVersion",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                    null, null, new object[] { true });

                if (clarionVersion == null)
                {
                    Log("Versions.GetVersion(true) returned null");
                    return null;
                }

                var cvType = clarionVersion.GetType();
                var config = new ClarionVersionConfig();

                config.Name = cvType.GetProperty("Name")?.GetValue(clarionVersion) as string;
                config.BinPath = cvType.GetProperty("Path")?.GetValue(clarionVersion) as string;

                // Libsrc is a semicolon-delimited string
                string libsrc = cvType.GetProperty("Libsrc")?.GetValue(clarionVersion) as string;
                if (!string.IsNullOrEmpty(libsrc))
                    foreach (var p in libsrc.Split(';'))
                    {
                        var t = p.Trim();
                        if (!string.IsNullOrEmpty(t)) config.LibSrcPaths.Add(t);
                    }

                // RedirectionFile is a RedirectionVersion object with a Name property
                var redVersion = cvType.GetProperty("RedirectionFile")?.GetValue(clarionVersion);
                if (redVersion != null)
                {
                    config.RedFileName = redVersion.GetType().GetProperty("Name")?.GetValue(redVersion) as string;

                    // Macros is ReadOnlyCollection<KeyValuePair<string,string>>
                    try
                    {
                        var macros = redVersion.GetType().GetProperty("Macros")?.GetValue(redVersion);
                        if (macros is System.Collections.IEnumerable enumerable)
                            foreach (var item in enumerable)
                            {
                                var kvType = item.GetType();
                                var k = kvType.GetProperty("Key")?.GetValue(item) as string;
                                var v = kvType.GetProperty("Value")?.GetValue(item) as string;
                                if (!string.IsNullOrEmpty(k)) config.Macros[k] = v ?? "";
                            }
                    }
                    catch { /* macros are optional */ }
                }

                Log("Detecting Clarion version, exe=" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                Log("Active version: " + config.Name + ", BinPath=" + config.BinPath);
                Log("LibSrcPaths count=" + config.LibSrcPaths.Count);
                foreach (var p in config.LibSrcPaths) Log("  libsrc: " + p);

                return config;
            }
            catch (Exception ex)
            {
                Log("ClarionVersionService.Detect failed: " + ex.Message);
                return null;
            }
        }

        private static Type GetVersionsType()
        {
            try
            {
                var asm = Assembly.Load("Clarion.Core");
                return asm?.GetType("Clarion.Core.Options.Versions");
            }
            catch { return null; }
        }
    }
}
