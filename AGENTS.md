# EOS agent guidance

Read `CODEX_HANDOVER.md` and `CONTEXT.md` before broad repository changes. Preserve the existing EOS architecture and product constraints unless the task explicitly requires changing them.

## .NET work: retrieval before invention

For C#/.NET changes, use [Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) as the primary specialist guidance source when available. Inspect the affected EOS code and tests first, then consult only the skills relevant to the change. Do not mechanically apply every recommendation.

Route work as follows:

- C# implementation/refactoring -> `modern-csharp-coding-standards` / `csharp-coding-standards`
- public contracts, interfaces, abstractions, compatibility -> `api-design` / `csharp-api-design`
- EF Core / SQLite queries, tracking, migrations -> `efcore-patterns`; add `database-performance` when query performance is involved
- dependency injection -> `microsoft-extensions-dependency-injection`
- configuration/options -> `microsoft-extensions-configuration`
- nullable contracts -> `csharp-nullable-reference-types`
- concurrency/async coordination -> `csharp-concurrency-patterns`
- package/version changes -> `package-management`
- project/solution layout -> `project-structure`
- complex logic whose maintainability is uncertain -> `crap-analysis`
- substantial LLM-authored or LLM-refactored changes -> `slopwatch`

Ponytail and dotnet-skills are complementary: use Ponytail to expose duplication, dead code, and structural complexity; use the relevant .NET skill to decide whether and how the structure should change safely.

## Engineering guardrails

- Prefer the smallest root-cause change that preserves existing behavior and contracts.
- Apply Chesterton's Fence to public APIs and architectural seams: trace callers and understand why a boundary exists before replacing it.
- Do not split interfaces merely because they are large; split only when callers or capability semantics justify it.
- Do not enable EF Core `NoTracking` globally without auditing every mutation path that depends on tracked entities.
- Keep database filtering/projection in SQL when it is naturally translatable; do not force provider-hostile domain normalization into SQL for tiny reference/configuration sets.
- Preserve `CancellationToken` propagation through async I/O and database paths.
- Preserve repository compiler gates in `Directory.Build.props`: nullable analysis, warnings-as-errors, latest analysis level, and language version.
- Manage NuGet packages with the `dotnet` CLI. Do not hand-edit package versions or introduce mixed central/inline version management. If Central Package Management is introduced, validate restore/build across the complete solution.
- Do not suppress warnings, skip tests, swallow exceptions, insert arbitrary delays, or weaken quality gates to obtain a green build.
- Avoid new dependencies when the BCL or an already referenced package solves the problem cleanly.

## Validation

After meaningful .NET changes:

1. Build the complete solution on the appropriate platform.
2. Run affected tests, then the full test suite when practical.
3. Use CRAP analysis when changing complex business logic or test coverage.
4. Run Slopwatch after substantial AI-generated/refactored changes.
5. For WPF/WebView2/UI changes, use the existing Windows visual-validation pipeline; compilation alone is not product validation.

If specialist guidance conflicts with an established EOS constraint, keep the EOS constraint and document the reason rather than forcing the generic recommendation.
