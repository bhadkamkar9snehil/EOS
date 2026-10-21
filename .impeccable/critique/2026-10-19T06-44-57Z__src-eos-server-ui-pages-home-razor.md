---
target: Command Center
total_score: 24
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
target_identity: "file:C:\\Users\\Admin\\Documents\\Office\\EOS_V2\\src\\EOS.Server\\Ui\\Pages\\Home.razor"
target_fingerprint: "sha256:4ad2287c824dd87b0cac0b4333dcdb38d8eb297656e7879d637530de9d103575"
target_path: "C:\\Users\\Admin\\Documents\\Office\\EOS_V2\\src\\EOS.Server\\Ui\\Pages\\Home.razor"
timestamp: 2026-10-19T06-44-57Z
slug: src-eos-server-ui-pages-home-razor
---
DEGRADED: single-context (owner rule: no subagents for impeccable). Visual evidence = owner screenshot, dark theme, 2000px.

Score 24/40 (Acceptable). H1 3, H2 3, H3 3, H4 2, H5 3, H6 3, H7 2, H8 2, H9 2, H10 1.

P0 Team load chart is empty and oversized: capacity 1,291 h vs ~24 h allocated renders 8 dashed empty boxes over half the screen; SVG viewBox scaling blows text to ~20px. Fix: fixed-height chart, utilisation % per week, or swap to "who is booked / who is free" bars; if allocation coverage < 10% say so in one sentence instead of charting.
P1 SVG type scale drifts: chart labels render 18-22px while page text is 12px (viewBox scales with width). Fix: fixed pixel height + preserveAspectRatio, or HTML/CSS bars.
P1 People card is 31 items of mostly informational "no allocation this month" noise and outranks real issues; Infrastructure 14 reads as worse than Delivery. Fix: count only Serious/Critical in the number, demote Info to a muted tail.
P1 Project bars contradict health: four red bars sit near the plan marker yet show "–" slip; reader cannot tell why they are at risk. Fix: label the gap ("-17 pts") at the marker, color by gap, show reason text on the row.
P2 Card copy is raw alert prose at 11-12px, truncated names, mono uppercase eyebrows; border-top accent on rounded cards (detector). Fix: one-line plain summary per item, drop top-border stripe.
Minor: big empty band under charts, chart heights mismatched; 30px / 2px off DESIGN.md scale; subline wraps 3 lines at 2000px.
