# EPA Visual Validation Workflow

This document defines the required visual validation method for EPA/EOS UI work.

## Principle

Visual work is not considered validated by code review, build success, or generated concept art. The UI must be rendered in a real browser and inspected from screenshots.

## Required loop

1. Build or update the UI implementation.
2. Run `scripts/capture-ui.ps1` against the compiled WPF + Blazor/WebView2 desktop application.
   Capture mode uses a deterministic synthetic database and redirects its SQLite/log files beneath
   the evidence directory; it never reads or mutates the user's live application data.
3. Capture screenshots from the actual desktop window at minimum:
   - 1920 x 1080
   - 1536 x 1024
   - 1280 x 800
4. Inspect screenshots for:
   - material hierarchy and visual fidelity
   - spacing, alignment and density
   - clipping and overflow
   - responsive behavior
   - chart legibility
   - typography collisions
   - missing assets or browser console errors
5. Compare against the active benchmark/reference image, not against the previous implementation.
6. Make corrections, rerender, and repeat until the visual pass is acceptable.
7. Only after real desktop-host evidence exists should a visual implementation be described as validated.

Azure Pipelines runs the same capture after build/tests and publishes the `visual-evidence` artifact,
including ten PNGs, `visual-report.json`, and the isolated structured EOS logs.

## Rendering hierarchy

Preferred order:

1. Compiled Windows desktop host with its embedded WebView2 surface.
2. Screenshots supplied from the running application at the target display sizes.
3. Browser-only rendering may be used for isolated diagnostics, but is not acceptance evidence for the desktop product.

## Evidence rules

- Do not use image generation as evidence of implemented UI quality.
- Do not claim desktop visual validation from compilation alone.
- Do not claim responsive quality without checking at least the required viewport sizes.
- Record visible defects before changing the benchmark interpretation.
- Preserve screenshots when they are useful for before/after comparison.

## Benchmarking rule

When a reference image is supplied, first identify its material and spatial hierarchy before copying colors or decoration. Match:

- dominant chassis/background material
- mounted panel material
- recessed vs raised elements
- border/bezel depth
- lighting direction
- control tactility
- chart treatment
- typography density
- whitespace and occupancy

A color that appears in a large area is not automatically a page background color; determine whether it represents a mounted plate, instrument face, inset surface, paper, enamel, painted metal, or another physical layer.
