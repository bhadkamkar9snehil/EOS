---
version: 1
slug: "src-eos-server-ui-layout-mainlayout-razor"
primary_target: "src/EOS.Server/Ui/Layout/MainLayout.razor"
related_targets: []
---

Scope: whole EOS V2 web UI (all routes). Visitor mode: Operate.

Audience and job: owner and up to four team leaders, daily long sessions, deciding who works on what and seeing what slips. Constraints: Blazor Server, static CSS, no build pipeline, no comments in code, no new tests.

Chosen direction: an international railway timetable and station signage system (assigned candidate 4 of the grounded list; assigned seed 00116654 candidate index 4). Memorable moment: finish-date slip reads like a departure board, a plain row of numbers where only deviation is coloured.

## Direction contract

THESIS: EOS reads like a station timetable: strict grid, ink on pale ground, colour only when something deviates. It refuses the dark-sidebar dashboard with rounded cards, icon glyphs and orange accent.
OWN-WORLD: cool near-white ground, white sheets, ink-black type and 2px section rules, one signal red for late or at-risk, one timetable yellow for the as-of date plate and attention cells, humanist sans (Segoe UI) with tabular numerals, rectangular 2px controls, no shadows, no gradients, no pictograms standing in as decoration.
STORY: the owner opens EOS and in seconds knows which projects are late, by how many days, who is overloaded; every number sits in a column that can be compared down the page.
FIRST VIEWPORT: black station band with EOS wordmark, a yellow as-of date plate and sign-out; a second graphite band with the grouped text navigation; below it the page title at left with actions at right on a 2px ink rule; then a ruled strip of ten key figures, then the project health table beside the alerts list.
FORM: Swiss/SBB-style timetable and wayfinding system, position 4 of the grounded list, seed key 00116654.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
