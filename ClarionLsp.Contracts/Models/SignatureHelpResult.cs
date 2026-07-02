namespace ClarionLsp.Contracts.Models
{
    /// <summary>
    /// Parameter hints for a call site (LSP textDocument/signatureHelp). Describes the
    /// overload(s) of the procedure/method being called and which parameter the cursor is on.
    /// </summary>
    public class SignatureHelpResult
    {
        /// <summary>One entry per overload (never null; may be empty).</summary>
        public SignatureInfo[] Signatures { get; set; }

        /// <summary>0-based index into <see cref="Signatures"/> of the active overload.</summary>
        public int ActiveSignature { get; set; }

        /// <summary>0-based index of the parameter the cursor is currently on.</summary>
        public int ActiveParameter { get; set; }
    }

    /// <summary>A single call signature/overload.</summary>
    public class SignatureInfo
    {
        /// <summary>The full signature text, e.g. "MyProc(LONG pId, STRING pName)".</summary>
        public string Label { get; set; }

        /// <summary>Optional human-readable documentation (may be null).</summary>
        public string Documentation { get; set; }

        /// <summary>The individual parameters, in order (never null; may be empty).</summary>
        public SignatureParameter[] Parameters { get; set; }
    }

    /// <summary>A single parameter within a <see cref="SignatureInfo"/>.</summary>
    public class SignatureParameter
    {
        /// <summary>The parameter label, e.g. "LONG pId". When the server reports the label as
        /// offsets into the signature, this is the resolved substring.</summary>
        public string Label { get; set; }

        /// <summary>Optional per-parameter documentation (may be null).</summary>
        public string Documentation { get; set; }
    }
}
