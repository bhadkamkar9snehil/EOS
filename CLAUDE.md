# EOS Claude Code guidance

Before broad changes, read and follow:

1. `AGENTS.md` — shared engineering and dotnet-skills routing rules.
2. `CODEX_HANDOVER.md` — operational, CI, Windows-validation, and product constraints.
3. `CONTEXT.md` — concise current product context.

For .NET work, follow the retrieval-led dotnet-skills routing in `AGENTS.md` rather than relying on generic framework advice. Ponytail findings are inputs to the audit, not automatic refactoring instructions: preserve behavior, trace callers, fix root causes, and validate the resulting design with the relevant .NET skill and tests.
