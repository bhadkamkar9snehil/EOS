# EOS design system — the material language

The visual target is **not** a modern Tailwind dashboard, and **not** retro
skeuomorphism. It is:

> a precision analytical instrument assembled from physical-looking components.

Ivory plates mounted into a graphite chassis. Charts recessed *into* those plates
rather than stacked on them as more boxes. Orange behaving like an illuminated
indicator rather than a decorative accent. Numbers that look printed onto the
panel. Tables that stay extremely legible and dense despite all the depth.

Everything below is defined in `src/EngineeringPerformance.UI/wwwroot/tailwind-input.css`,
which is the single source of truth. Chart JS reads the same CSS custom
properties via `getComputedStyle`, so UI and chart colours can never diverge.

---

## 1. The light source

Fixed at **upper-left (~10:30)**. This single rule is what keeps depth
believable:

- top edges catch a highlight
- bottom / bottom-right cast the shadow
- **recesses invert exactly that relationship**

Every shadow token obeys it. If you add one, it must too. Skeuomorphism looks
cheap the moment the lighting is inconsistent.

## 2. The Z-levels

| Level | Metaphor | Use | Class |
|---|---|---|---|
| −2 | cavity | chart wells, gauge bays | `well` |
| −1 | engraved recess | inputs, tracks, meters, empty slots | `track` |
| 0 | chassis | page frame, sidebar, header, footer | `chassis` |
| +1 | mounted plate | normal analytical panel | `plate` / `card` |
| +2 | raised control | button, selector, active tab | `control` |
| +3 | floating | popover, modal, tooltip | `floating` / `glass` |

Pick the level that describes the **object**, never the level that "looks about
right". `shadow-lg` on everything is the failure mode this system exists to
prevent.

## 3. Composition rule — the single most important one

Do **not** build pages as `canvas → card → card → card → card`.

Build them as:

```
page canvas  →  plate (analytical region)  →  wells (recessed instruments)
```

A chart is **a cavity machined into a plate**, not another card sitting on one.
Concretely:

```html
<section class="plate p-4">
  <div class="panel-head">
    <div>
      <div class="panel-eyebrow">Performance</div>
      <h2 class="panel-title">Performance trajectory</h2>
    </div>
    <span class="label-instrument ml-auto">12 months · 47 engineers</span>
  </div>
  <div class="well p-3">
    <div id="some-chart" class="h-[320px]"></div>
  </div>
</section>
```

Group related instruments onto **one** plate separated by hairlines
(`divide-y divide-line-soft`) instead of giving each its own border and shadow.

## 4. Surfaces

| Token | Use |
|---|---|
| `bg-canvas` | the bench everything sits on |
| `chassis` / `bg-chassis` | structural frame (sidebar, header, footer, table heads) |
| `bg-chassis-deep` | shaded/recessed chassis |
| `bg-surface` | the plate itself |
| `bg-surface-raised` | plate lifted toward viewer |
| `bg-surface-inset` | shallow inset region on a plate |
| `bg-well` / `bg-well-deep` | machined cavity |

Text on chassis uses `text-on-chassis`, `text-on-chassis-soft`,
`text-on-chassis-muted` — **never** `text-ink` (which is dark in light mode and
would vanish).

## 5. Typography roles

Use the **role**, not a pile of size/weight/case utilities. The failure mode is
everything landing on `text-xs font-semibold uppercase`, which flattens
hierarchy into mush.

| Role | Class | Notes |
|---|---|---|
| Display metric | `metric` + `text-metric{,-sm,-lg}` | display face, tabular, tight leading |
| Section title | `panel-title` | ~15px semibold, **not** uppercase |
| Eyebrow / category | `panel-eyebrow` | uppercase, tracked, muted |
| Instrument label | `label-instrument` | axis/column/gauge captions |
| Delta | `metric-delta` | pairs with a metric |
| Body / explanation | `text-xs` / `text-sm` | normal sentence case |
| Any digits | `tnum` or `tabular-nums` | apply to *all* numerics |

`uppercase` is reserved for **machine-voice labels**. Never uppercase ordinary
UI copy.

`font-display` is Bahnschrift (Windows' DIN 1451 grotesk — the typeface of
gauges and control panels), falling back to Segoe UI. It ships with Windows, so
there is no network fetch. Dense tabular content stays on `font-sans` (Segoe),
where its legibility wins.

## 6. Numbers are the primary visual object

This is performance software; it should be read through its numbers first.
Restructure metric blocks from `title → description → number` to:

```
number  →  delta/status  →  compact descriptor
```

```html
<div>
  <div class="metric text-metric">87</div>
  <div class="metric-delta text-good">+4.2 vs last month</div>
  <div class="label-instrument mt-1">Performance score</div>
</div>
```

Apply `tabular-nums` to every score, percentage, count, date, rank, delta and
duration — otherwise digits shimmer and shift as values update.

## 7. Orange is an indicator, not a colour

`--color-primary` is permitted in exactly **three** roles:

1. current / selected
2. the primary user action
3. one important analytical emphasis per view

Nowhere else. It is deliberately **absent from the categorical chart palette**
so it never competes with itself. If orange is on buttons *and* series *and*
borders *and* highlights, it stops meaning anything.

## 8. Semantic vs categorical colour

- **Semantic** (`good`, `warning`, `serious`, `critical`, `info`) carries
  *meaning*. Never use these as arbitrary series colours.
- **Categorical** (`chart-1`…`chart-8`) is deliberately cool and desaturated —
  petrol, blue, violet, plum, teal, cool grey. No red/green/amber/orange.

A series that borrows red creates real ambiguity: "is series 4 bad, or is it
just series 4?"

## 9. Tables

`.epa-grid` handles QuickGrid chrome. Severity is a **3px status rail**
(`row-flag`, `row-flag-critical`), never a full-row wash — flooding rows with
pale yellow/red makes dense tables noisy and fights the hover and selected
states for the same pixels. Full background is reserved for `row-selected`.

Right-align numeric columns. Header is chassis-coloured, sticky, small and
low-contrast — it should recede, not shout.

## 10. Motion simulates mass

Larger objects move slower. Never `transition-all duration-300` everywhere.

| Object | Duration |
|---|---|
| switch / lamp | ~110ms (`duration-100`) |
| button / control | ~140ms (`duration-150`) |
| card lift | ~180ms (`duration-200`) |
| drawer / panel | ~260ms (`duration-300`) |

Transition **specific** properties: `transition-[box-shadow,transform]`,
`transition-colors`, `transition-opacity`. Easing: `ease-press` for controls,
`ease-settle` for larger movement.

## 11. Interaction states

Every clickable object has three physical states. The **press** matters more
than the hover:

```html
<button class="control hover:control-hover active:control-pressed">
```

Travel is 1px each way. More reads as a toy. `.btn` already does this.

## 12. `group` for whole-instrument interaction

A panel should behave as **one instrument**, not five coincidentally adjacent
elements. Put `group` on the container and let one interaction drive children:

```html
<li class="group ...">
  <span class="opacity-0 transition-opacity group-hover:opacity-100">↗</span>
</li>
```

Use it to reveal secondary actions, expose benchmark ticks, brighten a rim, or
light a lamp.

## 13. `data-*` as the styling API

Prefer exposing **domain state** as attributes and styling off them, rather
than building long conditional class strings in C#:

```razor
<div data-band="@Analytics.ScoreBand(score)" class="data-[band=critical]:text-critical ...">
```

Useful attributes: `data-band`, `data-state`, `data-severity`, `data-density`,
`data-expanded`.

## 14. Container queries for component internals

Panels move between full-width, half-width and sidebar slots. A component
should react to **the space it was given**, not the viewport:

```html
<div class="@container">
  <div class="grid grid-cols-1 @md:grid-cols-2 @xl:grid-cols-3">
```

Rule of thumb: **viewport breakpoints → page architecture**, **container
queries → component internals**.

## 15. Effects — use sparingly and for a reason

- `drop-shadow-*` (not `shadow-*`) for SVG needles, markers, lamps, irregular shapes.
- `mask-*` gradients to fade sparkline tails, scroll edges, oversized background metrics.
- `backdrop-blur` **only** where the metaphor is genuinely glass (command
  palette, hover inspector, modal scrim). Never on dashboard panels.
- `text-shadow` only via `engraved` / `engraved-dark`, and only on large
  metrics or machine labels — on body copy it just looks blurry.
- Blend modes: at most a ~3% noise/soft-light layer. Texture must stay
  near-invisible.

## 16. Density

`--density-*` tokens drive `.epa-grid` and `.card` padding. Set
`data-density="compact"` on any subtree to tighten row height and padding
rather than hand-tuning `p-2`/`p-3`/`gap-2` per page.

## 17. Radii

Globally tightened: `rounded-xl` is now **7px** (was 12px). 12px+ is reserved
for genuinely floating assemblies. Prefer `rounded-md` (4px) for controls,
`rounded-lg` (5px) for wells, `rounded-xl` (7px) for plates. Full-round only
for pills, lamps and avatars.

## 18. Promote arbitrary values into tokens

Experimenting with `shadow-[0_7px_15px_...]` is fine. But the moment a value
appears **three times**, promote it to a token in `@theme static`. This is
exactly what was already done for `text-[10px]`/`text-[11px]` → `text-3xs` /
`text-2xs`.

---

## Conversion checklist

When converting a page:

- [ ] Replace `rounded-xl border border-line bg-surface p-4` with `plate p-4`.
- [ ] Wrap every chart container in a `well`.
- [ ] Give each panel a `panel-eyebrow` + `panel-title`; delete subtitles that
      merely restate the title.
- [ ] Convert the headline number of each panel to `metric` and lead with it.
- [ ] Add `tabular-nums`/`tnum` to every numeric cell.
- [ ] Remove orange from anything that is not selected-state, primary action, or
      the one analytical emphasis.
- [ ] Convert button/tab groups to `.segmented` or `control` + `active:control-pressed`.
- [ ] Convert status dots to `lamp` / `lamp-off`.
- [ ] Convert progress/score bars to `track` + a filled inner bar.
- [ ] Add `group` where a panel has hover-revealed actions.
- [ ] Check text on chassis uses `text-on-chassis*`.
- [ ] **Do not** hardcode hex colours, and do not add `dark:` variants for
      colour — the tokens already handle both themes.
