---
name: EOS V2
description: An engineering-operations command center set in the grammar of a railway timetable: ink on pale ground, hairline grid, colour only for deviation.
colors:
  ink: "#15181c"
  ink-soft: "#4b535c"
  ground: "#f2f3f4"
  paper: "#ffffff"
  band: "#15181c"
  band-raised: "#23282e"
  rule: "#c5cad0"
  rule-soft: "#e2e5e8"
  hover: "#eceef0"
  signal-red: "#d4001f"
  timetable-yellow: "#ffd400"
  amber-ink: "#8a5a00"
  link-blue: "#0a4d8c"
  focus-blue: "#0a6cff"
  done-grey: "#98a1ab"
typography:
  display:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "26px"
    fontWeight: 650
    lineHeight: 1.15
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 650
    lineHeight: 1.45
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "12.5px"
    fontWeight: 600
    lineHeight: 1.4
rounded:
  control: "2px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "14px"
  lg: "24px"
  xl: "30px"
components:
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.control}"
    height: "32px"
    padding: "0 14px"
  button-secondary:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    height: "32px"
    padding: "0 14px"
  input:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    height: "32px"
    padding: "0 8px"
  badge-risk:
    backgroundColor: "{colors.signal-red}"
    textColor: "{colors.paper}"
    rounded: "{rounded.control}"
  badge-attention:
    backgroundColor: "{colors.timetable-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
---

# Design System: EOS V2

## Overview

**Creative North Star: "The Departure Board"**

EOS reads like a station timetable. Everything hangs on a strict grid of ink-black rules and hairlines over a pale, cool ground, set in a humanist sans with tabular numerals so every column of days, hours and percentages lines up for comparison. Colour is information, not decoration: ordinary state is plain ink, and only deviation — a late project, an over-allocated engineer, a pending approval — earns red or yellow.

The surface is dense and calm. There are no cards, shadows, gradients, rounded containers or pictograms standing in for content. Sections are separated by a 2px ink rule and a heading; rows by a 1px hairline. The product is used for long daily sessions in a bright office, so the world is light, high-contrast and low-ornament.

**Key Characteristics:**
- Ink on cool pale ground; one signal red and one timetable yellow, nothing else saturated.
- Tabular numerals throughout; numbers right-aligned in their columns.
- Sections ruled, not boxed; no shadows, no nested containers.
- Black station band for identity and the as-of date plate; graphite band for navigation.
- Rectangular controls with a 2px corner.

## Colors

A near-monochrome ink palette with two signals, used only for deviation and attention.

### Primary
- **Ink Black** (#15181c): text, the station band, section rules, primary buttons, summary bars on the Gantt.

### Secondary
- **Signal Red** (#d4001f): late or at-risk state, critical-path bars, the today line, over-allocated cells, error toasts. Never used for decoration or for ordinary links.
- **Timetable Yellow** (#ffd400): the as-of date plate, moderate-risk or attention badges, stretched-load cells, the active-navigation underline, text selection.

### Neutral
- **Cool Ground** (#f2f3f4): the page. **Paper White** (#ffffff): inputs, the login sheet, and the Gantt canvas.
- **Soft Ink** (#4b535c): secondary text, labels, column headers.
- **Rule** (#c5cad0) and **Hairline** (#e2e5e8): control borders and row dividers; **Row Hover** (#eceef0).
- **Graphite Band** (#23282e): the navigation band. **Done Grey** (#98a1ab): completed work on the Gantt.
- **Link Blue** (#0a4d8c) for text links; **Focus Blue** (#0a6cff) for the keyboard focus ring only. **Amber Ink** (#8a5a00) for warning numbers on pale ground.

### Named Rules
**The Deviation-Only Rule.** Colour appears only where something deviates from plan. A screen full of healthy work is nearly monochrome; red and yellow are rare enough to be read at a glance.

## Typography

**Display, Body and Label Font:** Segoe UI Variable Text (with Segoe UI, system-ui, sans-serif). One humanist family in several weights; no second face.

**Character:** Plain, wayfinding-sign legibility. Hierarchy comes from weight and size steps, never from letter-spaced capitals.

### Hierarchy
- **Display** (650, 26px, 1.15, -0.015em): page titles, one per page.
- **Headline** (650, 15px): section headings in sentence case.
- **Body** (400, 14px, 1.45): tables and prose, prose capped near 72ch.
- **Label** (600, 12.5px): column headers, form labels, badges, legends.

### Named Rules
**The Tabular Rule.** Numerals are always tabular and numeric columns right-aligned, so a column of slips or hours can be scanned straight down.

## Layout

A single full-width column up to 1520px, 24px page padding, with a two-band header: the black station band (EOS wordmark, yellow as-of date plate, sign-out) over the graphite navigation band, grouped by thin vertical separators. Each page opens with a title block on a 2px ink rule, then ruled sections. Two-up sections use a 32px gutter and stack below 1100px. The key-figure strip is a ruled grid of five columns that falls to two on narrow screens. Forms stack to a single column below 700px and the navigation scrolls sideways.

## Elevation & Depth

Flat. Depth is conveyed by rules and tone, never by shadow: the 2px ink rule opens a section, hairlines separate rows, and the paper-white Gantt canvas sits on the cool ground. The only overlay is the bottom-right toast, a flat ink block outlined in yellow.

### Named Rules
**The No-Shadow Rule.** No box shadows, blurs or gradients anywhere. Emphasis is a heavier rule or a signal colour.

## Shapes

Rectilinear. Controls and badges carry a 2px corner; everything else is square. Selection and hover are tonal fills. Bars on the Gantt are square-ended except task bars at 3px; summary bars end in small ticks, milestones are diamonds.

## Components

### Buttons
- **Shape:** rectangular, 32px high, 2px corner.
- **Primary:** Ink Black fill, white text, 14px horizontal padding.
- **Secondary:** Paper White with a 1px ink border; hover fills Row Hover.
- **Icon/inline:** 24px high, hairline border, for row actions.
- **Focus:** a 2px Focus Blue ring offset 2px.

### Badges
- Rectangular, 2px corner, 12.5px. Plain hairline for healthy, Timetable Yellow for attention, Signal Red with white text for risk.

### Inputs / Fields
- Paper White, 1px mid-grey border, 2px corner, 32px high; the as-of date field is a Timetable Yellow plate in the band. Inline table cells are borderless until hover or focus.

### Navigation
- Graphite band of text links, 13.5px, 38px high; the active page is white with a 3px Timetable Yellow underline. Groups are separated by a thin vertical rule.

### Key-figure strip
- A ruled grid, not cards: a 30px tabular figure over a 12.5px label per cell; the figure turns Signal Red or Amber Ink only for a deviation.

### Gantt (signature component)
- SVG timeline on Paper White with a label column. Task bars Slate (#3a4550), critical bars Signal Red, complete bars Done Grey, summaries Ink Black; baseline as a thin grey line; today as a Signal Red vertical line.

## Do's and Don'ts

### Do:
- **Do** keep ordinary state plain ink and let red (#d4001f) and yellow (#ffd400) mark deviation only.
- **Do** align every numeric column right with tabular figures.
- **Do** separate sections with a 2px ink rule and rows with a 1px hairline.
- **Do** keep controls rectangular with a 2px corner and a visible 2px focus ring.

### Don't:
- **Don't** use cards, nested containers, shadows, gradients or glass.
- **Don't** use coloured side-stripes on rows or callouts, icon glyphs as decoration, or a second typeface.
- **Don't** colour healthy states green; healthy is ink.
- **Don't** put a kicker or eyebrow label above a heading.
