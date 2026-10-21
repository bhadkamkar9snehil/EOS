@AGENTS.md

## Claude Code only
- Project knowledge and memory go to and come from gbrain (see "Knowledge and memory: gbrain" above). The tools are
  `mcp__gbrain__*`; if they are deferred, load them with ToolSearch before the first call. Do not keep durable project
  facts in Claude auto memory; save them to gbrain `eos-v2/<topic>` instead.
- The shared global instructions (`~/.codex/AGENTS.md`, imported by `~/.claude/CLAUDE.md`) still apply; this project's
  rules win where they differ.
