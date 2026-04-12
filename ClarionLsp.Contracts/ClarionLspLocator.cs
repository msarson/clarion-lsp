namespace ClarionLsp.Contracts
{
    /// <summary>
    /// Static service locator. The ClarionLsp addin sets Current on startup.
    /// Consumer addins null-check before use — no hard dependency required.
    /// </summary>
    public static class ClarionLspLocator
    {
        public static IClarionLanguageClient Current { get; set; }
    }
}
