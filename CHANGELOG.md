# Changelog

All notable changes to ClarionLsp will be documented in this file.

## [1.0.0] - 2026-03-17

### Added
- Initial public release
- LSP server lifecycle management — auto-discovers the highest installed version of `msarson.clarion-extensions` from `%USERPROFILE%\.vscode\extensions`
- `IClarionLanguageClient` public interface in `ClarionLsp.Contracts.dll` (no SharpDevelop dependency) so other addins can consume LSP features without coupling to SharpDevelop
- Hover support — rich hover tooltips for procedures, classes, variables, and built-in functions
- Go to Definition — navigate to the declaration of any symbol
- Go to Implementation — navigate to the implementation of methods and procedures
- Find All References — list all usages of a symbol across the solution
- Workspace Symbols — quick-open any procedure or class in the workspace
- Rename — rename a symbol and all its references across all open files
- Separate `ClarionLsp.Contracts.dll` deployment so consumer addins can reference the interface without the full SharpDevelop-coupled implementation
