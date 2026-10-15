# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
The owner (an engineering manager at an industrial MES/automation software company, SSM) and up to four team leaders. They sit at a desk, on the office network or Tailscale, and use EOS all day to run delivery: who is on which project, what slips, who can move where. Engineers themselves do not log in. Everyone who signs in sees everything. (Inferred from the owner's brief; no interview was held because the owner asked for judgement over questions.)

## Product Purpose
EOS V2 is the owner's engineering-management command center: projects and their WBS plans, a combined Gantt of every project, engineer assignments and load, what-if moves of an engineer or a skill between projects with the effect on every finish date, skills and training (ratings changed only by the owner), demand and pipeline, servers and VMs per project, alerts and reports. Success is that the owner can see the whole delivery picture in seconds and decide a move with its consequences in front of them.

## Positioning
It computes the consequence of resourcing decisions (effort-driven scheduling, finish-date slip across all projects) over the company's own projects, engineers, skills and VMs. A generic project tool cannot show that.

## Operating Context
Dense tabular and timeline work: WBS grids with inline editing, an SVG Gantt over many projects, week-by-engineer load heatmaps, scenario tables of finish-date slip in days. Sessions are long and repeated daily. Data comes from the company's workbooks (agility, training, ERP master, task tracker, VM inventory) and is edited in place.

## Capabilities and Constraints
Blazor Server pages inside EOS.Server; static CSS in wwwroot/css/eos.css; no frontend build pipeline is wired in yet (Tailwind is not actually integrated in V2). No code comments, no new tests. Hours, calendars, thresholds and vocabularies are all configurable data. No credentials are ever shown. Skill ratings are never automated.

## Brand Commitments
Product name: EOS. No logo or brand color has been confirmed by the owner.

## Evidence on Hand
Real imported data: 9 active projects, 28 engineers, 26 VMs, 462 ERP projects. No testimonials or marketing claims exist or are needed.

## Product Principles
Numbers first: scanning a table or a timeline beats any ornament. Consequence before commitment: every move shows its effect before anything is written. The owner's judgement is final: nothing changes skills or plans silently. Density is respect for an expert; clarity comes from alignment and hierarchy, not whitespace.

## Accessibility & Inclusion
Readable contrast for long daily use on office monitors; full keyboard operation of grids and forms.
