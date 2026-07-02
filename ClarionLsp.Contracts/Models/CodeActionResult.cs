namespace ClarionLsp.Contracts.Models
{
    /// <summary>
    /// A code action / quick-fix (LSP textDocument/codeAction). A Clarion action typically carries a
    /// <see cref="Command"/> (e.g. "clarion.addClassConstants") that must be run via
    /// <c>ExecuteCommandAsync</c> — the server then applies its changes through the
    /// <c>ApplyEditRequested</c> event. Some actions may instead carry an inline <see cref="Edit"/>.
    /// </summary>
    public class CodeActionResult
    {
        /// <summary>The user-facing title, e.g. "Add missing MyClass link equates to project".</summary>
        public string Title { get; set; }

        /// <summary>Optional kind: "quickfix", "refactor", "source", etc. (may be null).</summary>
        public string Kind { get; set; }

        /// <summary>True if the client should present this as the preferred action.</summary>
        public bool IsPreferred { get; set; }

        /// <summary>Inline edits to apply directly, or null when the action works via <see cref="Command"/>.</summary>
        public WorkspaceEditChange[] Edit { get; set; }

        /// <summary>The command to execute for this action, or null when it carries an inline <see cref="Edit"/>.</summary>
        public CommandInfo Command { get; set; }
    }

    /// <summary>A server command (LSP Command) — run it with <c>ExecuteCommandAsync(Command, Arguments)</c>.</summary>
    public class CommandInfo
    {
        /// <summary>The command's display title (may differ from the owning action's title).</summary>
        public string Title { get; set; }

        /// <summary>The command identifier, e.g. "clarion.addClassConstants".</summary>
        public string Command { get; set; }

        /// <summary>The command arguments, passed through verbatim to workspace/executeCommand
        /// (raw deserialized JSON values; never null but may be empty).</summary>
        public object[] Arguments { get; set; }
    }
}
