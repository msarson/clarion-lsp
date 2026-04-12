using System.Runtime.InteropServices;

namespace ClarionLsp
{
    internal static class Ods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern void OutputDebugString(string message);

        public static void Log(string msg)
        {
            OutputDebugString("[ClarionLsp] " + msg);
        }
    }
}
