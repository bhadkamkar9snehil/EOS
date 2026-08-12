# EPA Visual Validation Workflow

This document defines the required visual validation method for EPA/EOS UI work.

## Principle

Visual work is not considered validated by code review, build success, generated concept art, or a browser-only approximation. The UI must be rendered through the real Windows desktop host and inspected from screenshots.

For EOS, visual validation is now an automated part of the Azure Windows CI path. The build VM is not only a compiler; it is a reproducible visual lab for the WPF + Blazor/WebView2 product.

## Required loop

1. Change the implementation — for the Tailwind system, the styling source of truth remains `src/EngineeringPerformance.UI/wwwroot/tailwind-input.css`.
2. Azure Pipelines builds and tests the complete Windows solution on the self-hosted VM.
3. The desktop host starts in `EOS_VISUAL_CAPTURE=1` mode against deterministic synthetic data. It never reads the user's normal local SQLite database.
4. The real WebView2 surface is captured to PNG at minimum:
   - 1536 x 1024
   - 1280 x 800
5. Capture the important analytical routes, not one flattering dashboard screenshot. The baseline set is:
   - Overview
   - Employee Portrait
   - Timesheets
   - Peer Insights
   - light mode at both target sizes
   - dark mode on the densest analytical routes
6. Collect machine-readable browser diagnostics alongside the images:
   - JavaScript errors and unhandled rejections
   - `console.error` calls
   - horizontal overflow
   - plates clipped outside the viewport
   - chart canvas/SVG presence
   - visible text below the 11 px readability floor
   - resolved core design tokens
7. Collect the capture-mode application logs from the same run.
8. Inspect the screenshots for:
   - material hierarchy and visual fidelity
   - spacing, alignment and density
   - clipping and overflow
   - responsive behavior
   - chart legibility and annotation
   - typography collisions and weak hierarchy
   - missing assets or browser errors
   - light/dark surface separation
9. Compare against the active benchmark/reference image and the design-system intent, not merely against the previous implementation.
10. Correct the Tailwind/Razor/chart source, rerun Azure CI, inspect the new evidence, and repeat until the visual pass is acceptable.

## Evidence produced by CI

`./scripts/capture-ui.ps1` drives the capture run. The desktop host itself uses WebView2's capture API, so the screenshots are of the actual embedded browser surface users see.

Azure publishes the complete evidence directory as the `visual-evidence` Pipeline Artifact. It contains:

- PNG screenshots
- `visual-report.json`
- capture-mode app data/log directory
- `capture-failure.txt` or `startup-failure.txt` if the host cannot complete capture

CI also attempts to mirror the latest evidence to the non-CI `ci-evidence` GitHub branch. That mirror exists so automated review tooling can inspect the real PNGs directly without a person manually downloading Azure artifacts. The Azure Pipeline Artifact remains authoritative if the mirror cannot push.

## Rendering hierarchy

Preferred order:

1. Compiled Windows desktop host with its embedded WebView2 surface — automated CI capture or interactive run.
2. Screenshots supplied from the running application at the target display sizes.
3. Browser-only rendering may be used for isolated diagnostics, but is not acceptance evidence for the desktop product.

## Evidence rules

- Do not use image generation as evidence of implemented UI quality.
- Do not claim desktop visual validation from compilation alone.
- Do not claim responsive quality without checking at least the required viewport sizes.
- Do not use real employee/user data merely to create CI screenshots; deterministic synthetic evidence is safer and more reproducible.
- Record visible defects before changing the benchmark interpretation.
- Preserve useful before/after evidence when a visual change is consequential.
- A successful screenshot command is not enough: the image itself must be inspected.

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
