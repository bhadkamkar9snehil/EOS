# EPA Visual Validation Workflow

This document defines the required visual validation method for EPA/EOS UI work.

## Principle

Visual work is not considered validated by code review, build success, or generated concept art. The UI must be rendered in a real browser and inspected from screenshots.

## Required loop

1. Build or update the UI implementation.
2. Render the actual HTML/CSS/SVG/JS surface with deterministic dummy data when production data is not required.
3. Use Chromium controlled programmatically through Playwright in the ChatGPT runtime whenever available.
4. Capture fixed desktop screenshots at minimum:
   - 1536 x 1024
   - 1280 x 800
5. Inspect screenshots for:
   - material hierarchy and visual fidelity
   - spacing, alignment and density
   - clipping and overflow
   - responsive behavior
   - chart legibility
   - typography collisions
   - missing assets or browser console errors
6. Compare against the active benchmark/reference image, not against the previous implementation.
7. Make corrections, rerender, and repeat until the visual pass is acceptable.
8. Only after browser evidence exists should a visual implementation be described as validated.

## Rendering hierarchy

Preferred order:

1. Local ChatGPT runtime: Chromium + Playwright, rendering the Visual Lab directly.
2. Vercel preview hosting for a stable public render target.
3. Cloudflare Browser Rendering / Pages as a secondary remote rendering path when available.
4. Self-hosted Windows runner only when local/remote cloud rendering cannot reproduce the target environment.

## Visual Lab

The `visual-lab/` harness exists to provide deterministic, browser-hosted rendering of the actual EPA stylesheet/DOM contract without requiring local SQLite data or the WPF/WebView2 host.

The harness should stay structurally aligned with the production Razor markup and load the same relevant stylesheet stack. It is a visual QA surface, not a separate design implementation.

## Evidence rules

- Do not use image generation as evidence of implemented UI quality.
- Do not claim browser validation from compilation alone.
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
