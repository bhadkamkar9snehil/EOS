# AGENTS.md — EOS V2

Read `docs/SPEC.md` before changing behaviour. If code and spec disagree, fix one of them in the same change.

## Rules
1. **Nothing is hard-coded.** Hours, calendars, thresholds, scales, weights and vocabularies are settings
   (`EOS.Domain.Settings`). Code may carry **seed defaults** only, in `SettingDefaults`.
2. **Skill ratings change only by explicit human action** (spec §9.5). No import, formula or event may write a rating.
3. **No secrets.** Importers read whitelisted columns only; never read/store/log username/password columns
   (spec §24.9). Do not paste credentials from source workbooks anywhere.
4. **Scenarios never mutate plans.** Only an approved scenario promotes to a new baseline (spec §15.9).
5. **The scheduling engine is pure and deterministic** (no clock, no I/O, no randomness). Its golden tests
   (spec §28.1) must pass exactly.
6. **Same capability through UI, API, CLI and MCP.** Add the CLI mapping when you add a service method.
7. Every write is audited (who, when, before/after).

## Workflow
- Build/test: `dotnet build EOS.slnx` then `dotnet test EOS.slnx`. Warnings are errors.
- One commit per iteration: `<area>: <change>`. Annotated tags `vX.Y.Z` for releases.
- GitLab is self-hosted (`ssm/eos-v2`); CI runs on our runners only. No GitHub Actions or hosted CI.
- Remote: `http://100.110.236.122:8080/ssm/eos-v2.git` (default branch `main`).

## Vendored code
`third_party/meridianplan` is a snapshot of an MIT project. Keep its `LICENSE`. Record modifications in
`third_party/meridianplan/NOTICE.eos` and git history.
