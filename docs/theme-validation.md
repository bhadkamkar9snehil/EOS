# Theme and readability validation

This document defines the acceptance matrix for EOS appearance changes. Theme personality may change; semantic meaning, readability and information hierarchy may not.

## Automated runtime audit

Open the desktop app with developer tools attached and run:

```js
epaThemeAudit.run()
```

The audit checks the currently resolved theme for:

- required surface, foreground, semantic, focus and chart tokens
- WCAG AA text contrast for primary analytical foreground/background pairs
- 3:1 focus-indicator contrast against canvas, card and raised surfaces
- duplicate categorical chart colours
- visible leaf text below the 10.5 px operational typography floor

To inspect the generated diagnostics for every palette without changing the selected theme:

```js
epaThemeAudit.allPalettes()
```

## Build and test

The application is Windows-first and targets .NET 10. Run from a Windows development machine:

```powershell
dotnet restore EngineeringPerformance.slnx
dotnet build EngineeringPerformance.slnx -c Release --no-restore
dotnet test EngineeringPerformance.slnx -c Release --no-build
```

Validate JavaScript syntax with Node.js:

```powershell
node --check src/EngineeringPerformance.UI/wwwroot/theme.js
node --check src/EngineeringPerformance.UI/wwwroot/theme-audit.js
node --check src/EngineeringPerformance.UI/wwwroot/charts.js
node --check src/EngineeringPerformance.UI/wwwroot/analytics-charts.js
```

## Route matrix

Capture and compare every route in at least the following themes:

- Graphite
- Sandstone
- High Contrast Light
- Amethyst Night
- Forest Night
- Solar Night
- High Contrast Dark

For each route, test an empty/initial state where applicable and a populated high-density state.

Routes:

1. Overview
2. Timesheets & approvals
3. Peer Insights
4. Reports
5. Data imports
6. Imported data
7. Employees & teams
8. Review templates
9. Scoring
10. Settings
11. Employee detail / spotlight

## Display matrix

Validate at:

- 1366×768
- 1920×1080
- 2560×1440 or ultrawide equivalent
- Windows scaling 100%
- Windows scaling 125%
- Windows scaling 150%

## Theme-specific checks

For each light and dark palette verify:

- body and secondary analytical text contrast
- selected navigation visibility
- input/select borders and disabled controls
- focus ring visibility on canvas, card, table and dialog surfaces
- card/base/raised/inset surface separation
- table header and alternate-row readability
- sticky identity-column separation while horizontally scrolled
- tooltip foreground/background contrast
- chart grid visibility without dominating the data
- categorical series remain distinguishable by colour and shape where applicable
- heatmap zero, missing and not-applicable cells remain distinct
- semantic success/warning/serious/critical states do not rotate meaning with theme
- calculated/derived columns remain recognizable without full-column orange fills

## Motion and density

Repeat key routes with:

- Comfortable density
- Compact density
- System motion
- Reduced motion

Reduced motion must suppress chart entrance animation and broad theme cross-fades. Compact density must not reduce operational text below 10.5 px.

## Content stress cases

Validate with:

- long employee names
- long team names
- long analytical card subtitles
- long table headers
- missing source data
- true numeric zero values
- large positive and negative reconciliation variance
- no alerts and many alerts
- no peer reviews and dense peer-review networks
- 1 month of history and 6+ months of history

## Print/export regression

Theme changes must not alter report calculation logic. Re-run the existing workbook/report tests and manually confirm that exported reports remain readable and are not dependent on a dark application theme.
