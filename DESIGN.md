---
name: EOS V2
description: A calm, dense engineering-operations workspace: icon rail, contextual sidebar, tabbed canvas, impact side panel and a thin status bar, in soft white panels on a cool grey ground.
colors:
  ink: "#14171a"
  ink-soft: "#5a626d"
  ink-faint: "#8a929d"
  ground: "#eef0f3"
  panel: "#ffffff"
  side: "#f7f8fa"
  line: "#e2e5ea"
  accent: "#5b5bd6"
  accent-soft: "#ecebff"
  green: "#1f8a5b"
  green-soft: "#e1f4ea"
  amber: "#9a6200"
  amber-soft: "#fff1d0"
  red: "#c9302c"
  red-soft: "#fde7e5"
  lavender: "#b5b5fe"
  pool: "#88cff2"
  dandelion: "#ffc45c"
  rasberry: "#ff98cf"
  lime: "#cfec6f"
  tangerine: "#ff9b66"
typography:
  display:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 650
    lineHeight: 1.15
    letterSpacing: "-0.015em"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, monospace"
    fontSize: "11px"
    fontWeight: 500
    letterSpacing: "0.06em"
rounded:
  control: "8px"
  panel: "14px"
  pill: "999px"
spacing:
  sm: "8px"
  md: "16px"
  lg: "24px"
components:
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.panel}"
    rounded: "{rounded.control}"
    height: "34px"
  button-secondary:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    height: "34px"
  panel:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.panel}"
    padding: "16px 18px"
---

# Design System: EOS V2

## Overview

**Creative North Star: "The Planning Desk"**

One workspace, five regions: a left icon rail for navigation, a contextual sidebar (engineer bench, filters), a tabbed centre canvas, a right impact panel, and a thin bottom status bar that carries the as-of date. Pages that do not need a sidebar or impact panel simply leave those regions out.

Surfaces are soft white panels with a hairline border on a cool grey ground. Status is expressed with tinted pills (green, amber, red) and small mono caps labels. Engineers carry a pastel colour wherever they appear, so a person is recognisable across bench, lanes and calendar.

**Key Characteristics:**
- Layout follows the supplied wireframe: left rail, sidebar, tabs, main content, right sidebar, bottom bar.
- Ink-black primary buttons, indigo only for selection and focus, red only for real problems.
- Pastel person colours (lavender, pool, dandelion, rasberry, lime, tangerine) on bars, avatars and chips.
- Mono small-caps labels for panel headings; tabular numerals everywhere.
- Drag, click-to-add and keyboard paths for every planning action; nothing is written until committed.

## Colors

### Primary
- **Ink** (#14171a): text, primary buttons, rail brand mark.
- **Indigo** (#5b5bd6) with soft tint (#ecebff): active rail item, selected rows, focus-adjacent emphasis.

### Semantic
- **Green** (#1f8a5b / #e1f4ea), **Amber** (#9a6200 / #fff1d0), **Red** (#c9302c / #fde7e5): healthy, caution, late or over capacity.

### Neutral
- **Ground** (#eef0f3), **Panel** (#ffffff), **Side** (#f7f8fa), **Line** (#e2e5ea); **Ink soft** (#5a626d) and **Ink faint** (#8a929d) for secondary text.

### Named Rules
**The Person-Colour Rule.** An engineer keeps one pastel from the person palette everywhere; pastels are never used for status.

## Typography

One humanist sans (Segoe UI Variable) for everything, with a monospace used only for small-caps panel labels and code. Hierarchy is weight and size: 24px titles, 15px section headings, 14px body, 12.5px labels.

## Layout

Rail 64px, sidebar 300px, right panel 340px, status bar 36px; the centre canvas scrolls on its own. Below 900px the regions stack. Cards use 16px gutters and 14px radius.

## Elevation & Depth

Flat with one soft shadow (0 1px 2px, 0 6px 20px at 4–5% black) on panels; overlays are flat dark tooltips and toasts.

## Shapes

Panels 14px, controls 8px, pills fully rounded, bars 5–6px. Avatars are circles.

## Components

### Buttons
Ink fill for the one primary action; white with a grey border for secondary; compact icon buttons for row actions.

### Tabs and segmented controls
A grey track with a white raised active segment.

### Rail
Icon-only, tooltip on hover, indigo tint on the active item.

### Planner (signature)
Engineer bench in the sidebar, WBS with timeline in the canvas, per-engineer lanes with draggable ends, impact panel with finish delta, moved tasks and engineer load meters.

## Do's and Don'ts

### Do:
- **Do** keep the five-region layout and leave unused regions empty.
- **Do** show consequences before commitment.
- **Do** use tinted pills for status and mono caps for panel labels.

### Don't:
- **Don't** use the person palette for status or red for decoration.
- **Don't** add paragraphs of explanatory text; labels and tooltips only.
- **Don't** nest panels inside panels.
