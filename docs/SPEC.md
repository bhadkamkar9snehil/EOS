# SSM Operational Agility & Engineering Management — Master Specification (EOS V2)

| | |
|---|---|
| **Status** | Draft 2 — 14 Oct 2026. Draft 1 plus the owner's decisions of 14 Oct (§4 principles 11–14, §8.7, §9.5, §16.7, §30). |
| **Owner** | Snehil Bhadkamkar |
| **Related author** | Srushti Gandhi (Operational Agility Framework v1/v2, Timeline Impact Analysis v1) |
| **Purpose** | One authoritative description of *what* SSM's operational-agility and engineering-management system must do, *why*, *how it calculates*, and *what data it uses*, so EOS V2 can be designed, built, tested and accepted against it. |
| **Scope** | Everything from opportunity to closure and AMC, from an engineering-management viewpoint: demand, supply, skills, training, allocation, scheduling, what-if analysis, resource movement, infrastructure, reporting. |
| **Not in scope** | Commercial invoicing, HR payroll, customer-facing project documents. They appear only where they feed an engineering decision. |

---

## 0. How to read this document

### 0.1 Confidence tags
Every non-trivial statement carries a tag so you can tell fact from proposal:

| Tag | Meaning |
|---|---|
| **[SRC]** | Stated in a source document, workbook or chat. The source is cited. |
| **[DER]** | Derived by me from source data (a calculation, a count, a reconciliation). Reproducible. |
| **[PRO]** | **Proposed** by this specification. Not yet decided by SSM. Needs approval. |
| **[OPEN]** | An unresolved question. Listed again in §30. |
| **[VER]** | A source claim that I **independently re-computed and confirmed**. |

### 0.2 Reading order
- *Leadership:* §1, §3, §5, §20 (reports), §21 (dashboards), §29 (roadmap), §30 (open questions).
- *Team leads:* §6–§19.
- *Builders:* §7 (data model), §14–§17 (engine), §24 (imports), §25 (platform), Appendices.

### 0.3 Conventions
- Dates are `dd Mon yyyy`. Hours are decimal. Percentages are stored as fractions (0.85) and displayed as `85%`.
- "Engineer" means any person who supplies engineering capacity, including trainees (GETs).
- "Project" means a unit of customer delivery with its own plan. "Task" means a schedulable item inside a project plan.
- No credential, password or secret from any source workbook is reproduced in this document. See §2.4 and §24.9.

---

## 1. Vision, problem and goals

### 1.1 The problem [SRC]
SSM's operational challenge is a **resource supply-and-demand problem**:
- Multiple projects generate changing *demand* for engineering capabilities, each wanting a skill set for a period.
- SSM has a *finite* number of engineers with *finite* hours.
- Team leads keep separate Excel files with no standard structure; there is no consolidated cross-team view of demand vs capacity; future opportunities and future resource needs are hard to anticipate; leadership visibility depends on manual collection; allocation decisions are made case by case with no quick way to compare alternatives.
  (Operational Agility Framework, Draft v2, "Current state problem".)

### 1.2 The mental model — "Amazon for Engineers" [SRC]
| Marketplace concept | SSM interpretation |
|---|---|
| Customer demand | Project requirements and upcoming opportunities |
| Product / capability | Engineering skill or domain capability |
| Inventory | Engineers and their available working hours |
| Product attributes | Skill, proficiency, experience, role |
| Order quantity | Required people / required hours |
| Delivery window | Required project period |
| Fulfilment | Resource allocation and scheduling |
| Inventory planning | Capacity and future availability |

Worked example from the document: *Demand* = Python level 3, 20 h/week, 15 Oct–30 Nov. *Supply* = Engineer X, Python level 5, available 10 h/week between those dates. The framework must show that this is a **partial** fulfilment (10 of 20 h/week) and prompt a decision.

**Key principle [SRC]:** a capacity gap does **not** automatically mean hiring. It triggers investigation of other eligible resources, future availability, allocation changes, schedule changes or other operational options.

### 1.3 What the owner wants [SRC — Teams chats 17 Sep–7 Oct and meeting notes]
- "Multiple projects will have their demands and want certain skill set for certain durations. There are limited number of hours and engineers. We need to balance the load and hence the agility in moving **engineers or rather skills** from project to project."
- This needs **tracking** of projects, upcoming opportunities, current engineer work, **performance** tracking, and **detailed cross-project scheduling**.
- MS Project is "a good baseline for us in solving various questions".
- The owner does not want tasks tied to engineers once complete — any engineer who can do the work should be able to (§13.6).
- The Excel process is the blueprint for software. That software is **EOS V2**, intended to be the owner's **main command center** and a complete engineering-management solution.

### 1.4 Goals
1. **See** — one live picture of projects, people, skills, training, infrastructure and their relationships.
2. **Balance** — match demand to supply, show gaps and overloads *before* they hurt.
3. **Decide** — evaluate "what if we move X?" with schedule-accurate answers in seconds, compare options, approve and record.
4. **Track** — every resource movement, skill change and training event as an auditable transaction.
5. **Report** — consistent, repeatable reports for leadership, leads and engineers.
6. **Automate** — import existing workbooks, expose everything through UI, API, CLI and MCP.

### 1.5 Non-goals (for now)
- Replacing MS Project for customer-contractual schedules (EOS V2 matches its semantics but is not a drop-in).
- Cost accounting or rate cards beyond optional hourly-rate fields.
- Automatic resource levelling in v1 (MeridianPlan, the reference engine, also omits it).

---

## 2. Source inventory

### 2.1 Primary sources
| # | Source | Date | Content | Reliability |
|---|---|---|---|---|
| S1 | `SSM Operational Agility Framework_Draft_version_2.docx` (Srushti Gandhi) | 14 Oct 2026 | Problem statement, marketplace analogy, data domains, workbook structure, MVP dashboard, M365 ecosystem, what-if purpose and rules, 8-step flow, 3 sample projects, 2 scenarios | Authoritative intent; examples are **illustrative** |
| S2 | `SSM_Operational_Agility_Framework_version_2.xlsx` (two identical copies) | 14 Oct 2026 | 12 sheets: Definitions, Engineers, Skills, Skill_Matrix, Skill_Summary, Running_Projects, Upcoming_Projects, Project_Demands, Resource_Allocation, Deliverables (empty), Change_Gap_Log, Dashboard (listed as "planned"; absent) | Names/teams **real**; skill ratings **real**; availability, projects, demands, allocations, dates **sample/dummy** |
| S3 | `Timeline_Impact_Analysis_v1.docx` | 14 Oct 2026 | The what-if method, rules, Scenario 1 (move without replacement), Scenario 2 (major push), planned Excel tabs | Authoritative method; numbers from 3 sample projects, **hand-verified** in §15.8 |
| S4 | Meeting notes (screenshot, Teams, 14 Oct) | 14 Oct 2026 | 17-line list of decisions/requirements (§5.1) | Primary requirement source |
| S5 | Teams chat with Srushti Gandhi (screenshots) | 17 Sep–7 Oct 2026 | Intent, allocation philosophy, proficiency questions, what-if brief | Primary intent source |
| S6 | `X-Force Training.xlsx` | 14 Oct 2026 | Module knowledge matrix (132 modules × 27 engineer columns), proficiency sheet, doubts log, dashboard, how-to-add-module guide | Real knowledge data; proficiency levels **not yet filled in** |
| S7 | `Engineer_Skill_Assessment_2026_LPP 1.xlsx` | 14 Oct 2026 | 1–5 ratings of 10 skills for the LPP engineers; summary | Real |
| S8 | `Project_Task_Tracking_Status.xlsx` (and `_4461` copy) | 14 Oct 2026 | Master status list; LPP project list (10 live); AMC ticket log; per-project task trackers (NTPC Korba CMS, DCM Shriram e-Logbook, Sohar Steel MES, Petronet LNG e-Logbook, Emami LIMS, LMEL MES, Dr Reddy's, Pidilite Roff, Pidilite Tennax) | Real, as of 17 Sep 2026 (tracker "Last updated") |
| S9 | `XForce_Project_Review.xlsx` | 14 Oct 2026 | 454-row project master (ProjectList), PM / Technical-Manager assignment sheet, review summary | Real (ERP export) |
| S10 | `X-Force ALL Server-VM Details.xlsx` | 14 Oct 2026 | Physical hosts and VMs with purpose, state, owner, backup/delete flags; DOW publish and demo details | Real; **contains plaintext credentials (§2.4)** |
| S11 | `SSM Operational Agility Framework_Final Draft.docx` and `SSM_Operational_Agility_Framework_version_1.xlsx` | 24 Sep / 5 Oct 2026 | Earlier versions (superseded by v2; not re-read for this draft) | Superseded |

### 2.2 Reference sources
- MeridianPlan (https://wieslawsoltes.github.io/MeridianPlan/, repository `wieslawsoltes/MeridianPlan`, **MIT**) — the scheduling UI/engine reference (§27).
- The current EOS V1 code base and database (performance, reviews, teams, custom employee columns).

### 2.3 Related files observed but not analysed
`N-1 – SSM Leadership Logbook`, `A-One HR Task…`, `Emami MES Action Plan.xlsx`, `JSW_Khopoli_*` resource/readiness schedules, `RM Scope & All Modules Schedule.xlsx`, `LMEL FAT.xlsx`, `L3 Deliverables RM Tracker`, `LV_LeaveSummaryforRP`, `RPwiseTimesheetUtilazationReport`, `List_Employee_Generalteam`. These are strong candidates for later imports (leave, timesheets, per-project schedules) — see §24.8.

### 2.4 Data-handling note: credentials in S10
`X-Force ALL Server-VM Details.xlsx` stores Windows administrator, SQL `sa` and application passwords **in plaintext** for many hosts, VMs and demo systems, some shared across several systems. **EOS V2 will never import, store, display or log any username/password column from this or any workbook.** Importers skip those columns by design (§24.9). Credentials belong in a secret store. [PRO]

---

## 3. Glossary

| Term | Definition |
|---|---|
| **Engineer** | A person who supplies engineering capacity (Engineer, Senior Engineer, Lead Engineer, GET). |
| **GET** | Graduate Engineer Trainee. |
| **Team** | Organisational grouping of engineers: *LPP Engineering, LPP GET, LPP TAC, Run-rate Engineering, Independent* (S2). EOS V1 already has a Teams concept. |
| **LPP** | The engineering group that owns projects in the tracker ("LPP" sheet). Expansion not stated in the sources. [OPEN] |
| **TAC** | Team name in S2 (`LPP TAC`); expansion not stated. [OPEN] |
| **Skill** | A coarse capability (e.g., SQL, Historian). 10 defined in S2. |
| **Module / sub-module** | A fine-grained X-Force product topic (e.g., *LV → Pass Parameter*). 132 defined in S6. |
| **Rating / proficiency** | A 1–5 level of an engineer on a skill (S2) or a named level on a module (S6, currently all "Beginner"). |
| **Resource (for a skill)** | An engineer with rating ≥ 2 on that skill. [SRC, S2 Definitions] |
| **Deployable (for a skill)** | An engineer with rating ≥ 3 — can work independently. [SRC, S2 Definitions] |
| **Competency %** | Average rating ÷ 5 across the engineers considered. [DER from S2 formulas] |
| **Demand** | A requirement line: *skill, minimum level, hours/week, start, end, location* for a project. |
| **Supply / capacity** | Engineer hours available in a period. |
| **Allocation** | Planned hours of an engineer on a project in a period. |
| **Available hours** | Hours an engineer can work in a month: *availability % × standard hours*. |
| **Planned hours** | Hours that *should* be planned on project work: *available × target utilisation*. |
| **Target utilisation** | Share of capacity to plan on project work (default 85%); the remainder is leave, training, admin. |
| **Task** | A schedulable piece of work with a work quantity and assignments. |
| **Work** | Total effort in hours a task needs. |
| **Assignment** | Engineer(s) working a task, each at an allocation %. |
| **Duration** | Working days a task takes: work ÷ (hours/day × effective engineers). |
| **Dependency** | A task can start only after (or with) another (FS/SS/FF/SF with lag). |
| **Critical path** | The chain of tasks that determines the project finish. |
| **Slack / float** | Days a task can slip without moving the project finish (total) or its successors (free). |
| **Baseline** | A saved copy of a plan used to measure change. |
| **Scenario** | A hypothetical change set applied to a baseline for comparison, never applied until approved. |
| **Move / transfer** | Reassigning an engineer (fully or partially, from a date) from one project to another. |
| **Major push** | A request to accelerate one project by pulling engineers from others. |
| **Replacement** | An engineer provided to the *source* project to backfill a move. |
| **Slip / gain** | Change in finish date vs baseline, in working days (+ later, − earlier). |
| **Master schedule** | Portfolio view: project names with their **level-1 tasks only**. |
| **WBS** | Work breakdown structure; each project has its own. |
| **Skill transaction** | A recorded event that changes or evidences an engineer's skill (assessment, completion, review). |
| **Training transaction** | A recorded training event (module learnt, doubt raised/solved, session attended). |
| **Doubt** | A learner's recorded question on a module, assigned to a mentor and tracked to resolution (S6 "Need to Discuss"; the source spells it "Doute"). |
| **DOW** | X-Force digital operations/logbook product line. Expansion not stated. [OPEN] |
| **MES / XMES / X-Batch / PIP / EMS / AIMS / CMS / LIMS** | X-Force product lines appearing in project names and the tracker. |
| **AMC** | Annual Maintenance Contract (support tickets under contract). |
| **FAT / SAT / SITC** | Factory Acceptance Test / Site Acceptance Test / Supply-Installation-Testing-Commissioning. |
| **ASD-STE100** | Simplified Technical English, the controlled-language writing standard. Appears in the meeting notes as "ASD STE 100"; interpreted as the documentation-writing standard. [OPEN — confirm] |

---

## 4. Guiding principles

1. **Excel is the blueprint, not the ceiling.** Every sheet, column, formula and colour-rule in S2 maps to an EOS V2 entity or calculation. Nothing in the workbook is silently dropped; where V2 deviates it is stated.
2. **Decentralised input, consolidated view.** The leads closest to the work maintain their data; the system consolidates for leadership. [SRC, S1]
3. **Skills over names.** Work is matched to *skill + proficiency + availability*, not to a named person, wherever the work allows. [SRC, S5]
4. **Everything is a transaction.** Skill changes, training events, resource movements, plan changes and decisions are append-only records with who/when/why, so history can be replayed and audited. [SRC, S4: "Maintain Skill Transactions", "Maintain Training Transactions"]
5. **Schedule semantics follow MS Project.** Where the sources are silent, behave as MS Project does (§14). [SRC, S4/S5]
6. **A scenario never mutates the plan.** What-if runs on a copy; only an approved decision becomes a new baseline. [SRC, S3 steps 7–8]
7. **Show the evidence.** Every computed number (a slip, a competency %, an overload) can be expanded to the inputs and rule that produced it (calculation trace), as MeridianPlan does for dates.
8. **Thin data is flagged, not trusted.** Values from samples, small *n*, or stale dates are marked as such (the EOS design critique already found "Tech 0.0" printed for missing data; V2 must never show a number where there is no data). [DER, EOS critique 14 Oct]
9. **No secrets in the system.** (§2.4)
10. **Same capability, four doors.** Everything available in the UI is available through the REST API, CLI (`eos`) and MCP.
11. **Everything is configurable.** *[SRC — owner, 14 Oct]* No hours, thresholds, calendars, scales, bands, weights, vocabularies or rules are hard-coded. Every value quoted in this document as a "default" (160 h, 85%, ratings ≥ 2 / ≥ 3, Mon–Sat 8 h, health thresholds, load bands, factor tables…) is a **setting** with a scope cascade and history (§8.7). Code contains defaults only as seed data.
12. **Plans are authored here or imported, then extended.** *[SRC — owner, 14 Oct]* A project's WBS and tasks can be created inside EOS, or imported from the ERP (or other sources), and tasks can always be added, edited and re-organised afterwards (§16.7).
13. **Skill progression is a human decision.** *[SRC — owner, 14 Oct]* The owner sets and changes skill ratings and levels manually. The system records, evidences and suggests; it **never changes a rating by itself** (§9.5).
14. **The people who use it build it.** *[SRC — owner, 14 Oct]* "We are the engineers building this." The spec is working material for the owner's own team, so ambiguity is resolved by the owner directly (Q13 closed).
15. **The owner alone decides.** *[SRC — owner, 21 Oct]* Moving an engineer, approving a scenario, changing a rating, a plan baseline, a setting: only the owner approves or changes these. Nobody else can influence them. No lead agreement is required and none is modelled. (Supersedes "both Project Leads agree", §15.7 / §17.4.)
16. **ERP data is imported; everything else is entered in EOS.** *[SRC — owner, 21 Oct]* Data that the ERP holds (projects, roster, timesheets, attendance and leave, utilisation, the ERP task list) comes in only by import (§24, I19). Data the ERP does not hold (skills, training, doubts, demand, allocations, plans beyond the ERP tasks, infrastructure, reviews) is created and edited in EOS itself, not in side workbooks. Workbook imports for those remain only as one-time bootstrap.
17. **Engineers will get access, to their own data only.** *[SRC — owner, 21 Oct]* The owner intends to give engineers access so they can fill in and use their own data. An engineer account can read and write only records about that engineer (self-assessment, peer reviews they give, training knowledge, doubts, leave requests); it can never change plans, allocations, scenarios, ratings, settings or anyone else's data. Roles (Owner, Engineer) must exist before the first engineer token is issued (§23).
18. **Saturday is a half day.** *[SRC — owner, 21 Oct]* Saturday officially ends at 1:30 pm: 4.5 working hours on the default calendar (9:00 to 13:30). Calendars carry hours per weekday, not only one hours-per-day figure (§8.4, §14).

---

## 5. Captured requirements

### 5.1 Meeting notes, line by line (S4)

| # | Note (verbatim intent) | What it means | Where specified |
|---|---|---|---|
| N1 | Available hours | Per-engineer monthly/weekly capacity net of availability % and leave | §8.1 |
| N2 | Planned hours | Hours to be planned = available × target utilisation; also hours already allocated | §8.2, §13 |
| N3 | Skill Rating formulae | Define how a skill rating is calculated (and kept current) | §9.4 |
| N4 | How does skill factor into engineer assignment and movement | Skill gates eligibility, ranks candidates, and scales productivity | §9.6, §13.4, §15.5 |
| N5 | Parallel projects | An engineer can be on several projects at once; allocations split hours and must not exceed capacity | §13.3 |
| N6 | Upcoming projects | Pipeline demand with win probability feeds forward capacity planning | §12.4 |
| N7 | What if multiple engineers have the same skill level | Tie-break rules for candidate ranking | §9.7 |
| N8 | How are we tracking resource movements | A movement ledger with effective dates, percentages, replacements, approvals | §17 |
| N9 | Separate projects will have their different/separate WBS/Schedule | Each project owns its plan; no shared global task list | §16.1 |
| N10 | Master Schedule will just contain individual project names and **level-1 tasks** beneath the projects (not further drill-down to level 2) | Portfolio Gantt is a roll-up showing L1 only; detail lives in the project plan | §16.2 |
| N11 | ASD STE 100 | Controlled-language standard for documentation/work instructions | §3, §30 |
| N12 | Maintain Skill Transactions | Skill ledger | §9.5 |
| N13 | Maintain Training Transactions | Training ledger | §10.6 |
| N14 | Interconnect skills and project scenarios | Scenarios must read skills (eligibility, factor) and write back (training/transfer effects) | §15.5, §9.8 |
| N15 | What If — engineer movement across projects | Scenario type: move | §15.2 |
| N16 | What If — **no limit** on how many engineers | A scenario may move any number of engineers at once | §15.3 |
| N17 | Work Done = calendar time since engineer moved onto that task × Factor (determined by skill set **and** experience) | Progress accrues by elapsed time × productivity factor | §14.7, §9.6 |

### 5.2 Decisions and intent from the Teams chats (S5)

| Date | Statement | Implication |
|---|---|---|
| 17 Sep | "Think of this framework like Amazon for engineers." Use SharePoint to keep docs; use the Planner in SharePoint to track tasks. | Market model (§1.2); M365 is today's ecosystem; EOS V2 replaces the manual Excel/SharePoint workflow over time |
| 17 Sep | Tracking of projects, upcoming opportunities, current engineer work, performance tracking and detailed cross-project scheduling | The six modules in §6–§20 |
| 26 Sep | Proficiency levels are something "we need to decide along with HOW to progress through the proficiency levels" | §9.3 (levels) and §9.5 (progression) are **[PRO]/[OPEN]** |
| 26 Sep | Q: if Person A finished task A and moved on to task B, and an issue appears in task A, is a new engineer assigned or is A pulled back? A: "Ideally I don't want to tie tasks to engineers once completed… any and all engineers should be able to solve it. How hard the issue may be to diagnose determines skills required AND proficiency needed. And then availability. If A CAN do it, he will, or else anyone who can should." | §13.6 — post-completion issues are **skill-matched work items**, not owned by the original engineer |
| 26 Sep | "Max skill level is 5?" | Yes: 1–5 (S2). Level descriptions for 2–4 are proposed wording; the source defines only 1 and 5. |
| 7 Oct | Brief for what-if: make 2–3 projects, assign engineers to tasks, follow MS Project; "one of the most important aspects"; Q1 "If I move Mahesh from Project 1 to 2, what's the impact on timeline if I don't give a replacement to Project 1"; Q2 "I want a major push on one project and want to see when is the earliest I can move various engineers from their respective projects" | §15 — the two canonical scenarios, verified in §15.8 |

### 5.3 Requirements added in this session [SRC — user instruction 14 Oct]
- Track **projects**, **engineers assigned**, and **what-if** analysis of moving an engineer **or a skill** between projects.
- A **combined Gantt** showing *all* projects together properly (§16).
- **VMs per project** that engineers work on (§18).
- **Skillset and training** (§9, §10).
- **The entire project-management flow from an engineering-management perspective**, with reports (§19, §20).
- Everything accessible through **CLI, API, MCP and web UI**, running as an always-on server on the Ubuntu VM, for the owner now and up to 4 team leaders later (§25).
- Teams exist in EOS; a team leader role is in scope; **all information is visible to all users** because access will be given only to team leaders, not engineers. No roles/permissions for now. [SRC]

---

## 6. Organisation and people

### 6.1 Teams and headcount [DER from S2 Engineers sheet]
| Team | Engineers | Notes |
|---|---|---|
| LPP Engineering | 13 (E001–E013) | Core delivery engineers, including 3 Lead Engineers |
| LPP GET | 3 (E014–E016) | Graduate Engineer Trainees |
| LPP TAC | 3 (E017–E019) | |
| Run-rate Engineering | 4 (E020–E023) | Run-rate / sustaining work |
| Independent | 1 (E024) | Availability 10% |
| **Total** | **24** | |

**[SRC, S2 note]** In the *Engineers* sheet only the names and teams are real; **Role, Location, Availability %, Status are sample filler** to be replaced. EOS V2 must therefore capture real values on first use and mark the imported ones "unverified".

### 6.2 Engineer roster (as in S2; role/location/availability are sample)
| ID | Name | Team | Role | Base | Avail % | Avail h/mo | Planned h/mo @85% |
|---|---|---|---|---|---|---|---|
| E001 | Abhinath Maurya | LPP Engineering | Engineer | Office | 100% | 160 | 136 |
| E002 | Chirag Dhimmar | LPP Engineering | Lead Engineer | Site | 100% | 160 | 136 |
| E003 | Deep Thakor | LPP Engineering | Engineer | Office | 50% | 80 | 68 |
| E004 | Deepak Upadhaya | LPP Engineering | Engineer | Office | 100% | 160 | 136 |
| E005 | Dharmendra Ahirwar | LPP Engineering | Engineer | Office | 50% | 80 | 68 |
| E006 | Lalji Bodar | LPP Engineering | Senior Engineer | Office | 100% | 160 | 136 |
| E007 | Aayush Patel | LPP Engineering | Engineer | Office | 80% | 128 | 108.8 |
| E008 | Ziaul Haque | LPP Engineering | Engineer | Site | 100% | 160 | 136 |
| E009 | Mahesh Udar | LPP Engineering | Lead Engineer | Office | 100% | 160 | 136 |
| E010 | Malek Mohammed Hasnain | LPP Engineering | Engineer | Site | 100% | 160 | 136 |
| E011 | Sagar Rana | LPP Engineering | Lead Engineer | Office | 100% | 160 | 136 |
| E012 | Tejas Kale | LPP Engineering | Engineer | Office | 50% | 80 | 68 |
| E013 | Tejas Khadke | LPP Engineering | Engineer | Site | 100% | 160 | 136 |
| E014 | Dharmesh Thorat | LPP GET | Graduate Engineer Trainee | Office | 100% | 160 | 136 |
| E015 | Utsav N Patel | LPP GET | Graduate Engineer Trainee | Office | 100% | 160 | 136 |
| E016 | Krishna Gelani | LPP GET | Graduate Engineer Trainee | Site | 100% | 160 | 136 |
| E017 | Twinkle Patel | LPP TAC | Engineer | Office | 50% | 80 | 68 |
| E018 | Dharati Patel | LPP TAC | Senior Engineer | Office | 50% | 80 | 68 |
| E019 | Priyanka Makwana | LPP TAC | Engineer | Office | 100% | 160 | 136 |
| E020 | Dev Patel | Run-rate Engineering | Engineer | Office | 100% | 160 | 136 |
| E021 | Devanshi Mehta | Run-rate Engineering | Engineer | Office | 50% | 80 | 68 |
| E022 | Karan Patel | Run-rate Engineering | Engineer | Office | 50% | 80 | 68 |
| E023 | Kartik Patil | Run-rate Engineering | Engineer | Office | 50% | 80 | 68 |
| E024 | Srushti Gandhi | Independent | Graduate Engineer Trainee | Remote | 10% | 16 | 13.6 |
| | **Total** | | | | | **3,008** | **2,556.8** |

**[VER]** 3,008 × 0.85 = 2,556.8 ✓.

### 6.3 Roles outside the engineer roster
From S8/S9 (project assignment fields):
- **Project Managers (commercial/delivery):** Brijesh Vasoya, Chirag Koladiya, Vivek Deshani.
- **Technical Managers / Technical Leads:** Sagar Rana, Snehil Bhadkamkar, Chirag Dhimmar, Prithwesh Vyas, Mahesh Udar.
- **Delivery leader:** column exists in S9 (`DeliveryLeader Name`).
- Note: *Chirag Koladiya* (PM) and *Chirag Dhimmar* (engineer/lead) are **different people**; person-matching must not merge on first name.

### 6.4 Identity problem and the alias layer [DER]
The same person appears under different spellings across sources:

| Concept | Example variants |
|---|---|
| Full name vs short name | "Aayush Patel" (S2) / "Ayush" (S6) / "Aayush" |
| Spelling | "Ziaul Haque" (S2) / "Ziual" (S6) |
| Initials in project trackers (S8) | `SR`, `MU`, `KG`, `DU`, `UP`, `DP`, `TP`, `DM`, `DR`, `NR`, `HM`, `DT`, `ZH`, `ALL`, `User` |
| AD usernames (S10 "Update" sheet) | `Krishna.Gelani`, `Utsav.Patel`, `Dahrmesh.Thorat` (sic) |
| People in S6 but not in S2 | Safwan, Krinav, Priyen, Udaykumar |
| People in S2 with no training row | (to be reconciled; S6 lists 27 names, S2 lists 24) |

Several initials are **ambiguous** (e.g., `DP` could be Dharati Patel or Dev Patel; `UP` Utsav Patel or Udaykumar P.; `DT` Dharmesh Thorat or Deep Thakor; `HM`, `NR`, `DR` unresolved). **[PRO]** EOS V2 keeps a `PersonAlias` table (alias, kind, source, person) and an **alias-resolution screen** that lists every unresolved token with its context (project, task) so a lead can map it once. Unmapped aliases never block an import; the affected rows are flagged "unresolved person".

### 6.5 Capacity-relevant person attributes [PRO]
`employment_type` (employee / consultant / trainee), `grade`, `base_location` (Office/Site/Remote), `primary_team`, `secondary_teams`, `joined_on`, `left_on`, `is_active`, `default_availability`, `calendar_id`. EOS V1 already carries *consultant*, *probation*, *non-billable*, *updown* flags; V2 keeps them and links V1 performance data to the same Person.

---

## 7. Domain model

The model is split into bounded contexts. Names are logical; physical schema follows in implementation. "FK" = foreign key. All tables carry `id`, `created_at`, `created_by`, `updated_at`, `updated_by`, `row_version`.

### 7.1 People and calendars
```
Team(id, name, leader_person_id, parent_team_id?)
Person(id, code, full_name, email, employment_type, grade, role_title, base_location,
       primary_team_id, joined_on, left_on, is_active, calendar_id,
       is_consultant, is_on_probation, is_non_billable, is_updown)     -- V1 flags kept
PersonAlias(id, person_id?, alias, kind[initials|short|ad_user|spelling], source, resolved)
Calendar(id, name, hours_per_day, work_days_mask[Mon..Sun], timezone)
CalendarException(id, calendar_id, date_from, date_to, working?, hours?, note)   -- holidays, overrides
Availability(id, person_id, date_from, date_to, availability_pct, reason[contract|leave|training|part_time|other], note)
Leave(id, person_id, date_from, date_to, hours?, kind, status)                    -- fed by leave/timesheet imports
```

### 7.2 Skills and training
```
Skill(id, code, name, category, description)                           -- 10 today (S2)
SkillLevel(level 1..5, name, meaning)                                  -- scale, editable
Module(id, product, module, sub_module, sort_order)                    -- 132 today (S6)
ModuleSkillMap(module_id, skill_id, weight)                            -- [PRO] roll-up of modules into skills
PersonSkill(person_id, skill_id, rating, source[lead|computed|blend], assessed_on, assessed_by, valid_until?)
PersonModule(person_id, module_id, state[Know|DontKnow|NeedToDiscuss], proficiency_level?, last_changed)
SkillTransaction(id, person_id, skill_id?, module_id?, kind, old_value, new_value, evidence_ref, by, at, note)
TrainingTransaction(id, person_id, module_id?, kind[learn|session|assessment|mentoring|doubt_raised|doubt_solved],
                    hours?, trainer_person_id?, at, note)
Doubt(id, module_id, sub_module_text, text, raised_by, raised_on, assigned_to?, assigned_on?,
      solved_on?, result[Open|Assigned|Hold|Solved], remarks)
TrainingPlan(id, person_id, module_id|skill_id, target_state/level, due_on, mentor_person_id?, status)
RequiredSkillLevel(scope[team|org|project_type], skill_id, required_pct, effective_from)   -- fills S2's empty "Skill Set Required %"
```

### 7.3 Projects, pipeline, delivery
```
Account(id, name, vertical?, area?)
Project(id, project_no, name, account_id, product_line, project_type, erp_status[CLS|RLS|TECO|FUTURE ORDER|HLD],
        tracker_status[Entered|Under Development|Hold|Deployed|Completed], pm_person_id, tm_person_id,
        delivery_leader, order_value, margin_pct, po_start, po_end, contractual_end, planned_end,
        site_location, parent_project_id?, health[OnTrack|ModerateRisk|AtRisk], progress_pct, last_updated)
Opportunity(id, code, name, lead_person_id, expected_value_lakh, expected_hours, likely_booking_month,
            duration_months, stage[Qualified|Proposal Sent|Negotiation|...], win_probability, status[Expected|Won|Lost],
            required_skills[], weighted_hours)
ProjectMonthHours(project_id|opportunity_id, month, hours, kind[planned|actual])
Milestone(id, project_id, name, planned_on, actual_on?, kind[FAT|SAT|Go-live|Payment|Other])
Deliverable(id, project_id, name, owner_person_id, due_on, status, ...)        -- sheet is empty in S2: structure to be agreed
RiskIssue(id, project_id, kind[risk|issue|dependency|assumption], text, severity, owner, due_on, status, mitigation)
AmcTicket(id, project_id, product, in_amc, raised_on, raised_by, issue, assigned_on, assigned_by,
          resolved_on, assigned_engineers[], root_cause, notes, status)          -- columns from S8 "AMC"
```

### 7.4 Scheduling
```
Plan(id, project_id, kind[live|baseline|scenario], name, version, based_on_plan_id?, scenario_id?, created_at, locked)
Task(id, plan_id, wbs_code, parent_task_id?, level, name, kind[task|summary|milestone], module?,
     work_hours, original_duration_d, remaining_duration_d, percent_complete, physical_percent?,
     start, finish, actual_start?, actual_finish?, constraint_kind, constraint_date?, calendar_id,
     priority, notes, early_start, early_finish, late_start, late_finish, total_float_d, free_float_d, is_critical)
TaskLink(id, plan_id, predecessor_task_id, successor_task_id, kind[FS|SS|FF|SF], lag_d)
Assignment(id, task_id, person_id|placeholder_skill, allocation_pct, work_hours, role[lead|engineer|reviewer], start?, finish?)
ScenarioPlanSnapshot(...)                -- see §15
```

### 7.5 Demand, allocation, movement, governance
```
Demand(id, project_id|opportunity_id, skill_id, min_level, hours_per_week, start, end, location, status[Open|Partially Filled|Filled|Gap - Escalated])
Allocation(id, person_id, project_id, demand_id?, role, status[Proposed|Confirmed|Released], date_from, date_to, hours_per_month{month->h})
ResourceMovement(id, person_id, from_project_id, to_project_id, effective_date, percent_moved, replacement_person_id?,
                 scenario_id?, reason, requested_by, approved_by?, approved_on?, status[Requested|Approved|Rejected|Executed|Reverted])
ChangeGapLogEntry(id, kind[Change Request|Capacity Gap|Decision|...], project_id, demand_id?, raised_by, raised_on, description,
                  gap_type[Proficiency mismatch|Capacity|Timing|...], impact_hours, impact_days, option_decision, approver,
                  decision_date, status[Open|Approved|Rejected|Closed])
Approval(id, subject_kind, subject_id, step, approver, decision, decided_on, comment)
AuditEvent(id, at, actor, action, subject_kind, subject_id, before_json, after_json)
```

### 7.6 Infrastructure
```
Host(id, kind[physical|vm], parent_host_id?, ip, hostname?, purpose, state[Running|Off|Unknown|NoAccess],
     os?, ram_gb?, cpu_cores?, logical_processors?, project_id?, owner_person_id?,
     need_backup?, marked_for_deletion?, removed?, notes)
HostDisk(host_id, drive_letter, free_gb, total_gb, captured_at)
Environment(id, host_id, product, customer_or_demo, system[DOW|X-Batch|...], internal_url?, external_url?, sql_host?, db_name?, purpose[publish|demo|poc|dev|test|prod])
ProjectHost(project_id, host_id, role[application|database|publish|test|demo], engineers[])
```
(No credential fields exist in this schema — by design, §2.4.)

### 7.7 Links to EOS V1 (performance) [PRO]
EOS V1 entities — `Employee`, `MonthlyPerformance`, `PeerReview`, scoring settings, custom employee columns — are re-homed under `Person`. Performance and peer-review data become a **People → Performance** tab and feed "capacity quality" signals (e.g., attendance discipline, timesheet fill) into planning, never the reverse (planning data must not silently alter performance scores).

---

## 8. Capacity: available hours and planned hours

### 8.1 Definitions and formulas [SRC — S2 Definitions and Engineers sheets]
| Quantity | Formula | Default inputs |
|---|---|---|
| **Standard hours / month** | global assumption | 160 (8 h × ~20 working days) |
| **Target utilisation** | global assumption | 0.85 |
| **Availability %** | per engineer, entered | 1.0, 0.8, 0.5, 0.1 … |
| **Available hrs / month** | `availability% × standard hours` | e.g., 0.5 × 160 = 80 |
| **Planned hrs / month** (at target utilisation) | `available hrs × target utilisation` | e.g., 80 × 0.85 = 68 |
| **Team / org capacity** | sum over engineers | 3,008 available; 2,556.8 planned [VER] |

The 15% gap between *available* and *planned* is deliberately left for leave, training, support, mentoring and administration.

### 8.2 Allocated, free and utilisation [DER/PRO]
For engineer *p*, month *m*:
```
allocated(p,m)  = Σ over projects of allocation hours (Confirmed + optionally Proposed)
free_planned    = planned(p,m)   − allocated(p,m)       # vs. target
free_available  = available(p,m) − allocated(p,m)       # vs. hard ceiling
load_vs_planned = allocated / planned
load_vs_avail   = allocated / available
```
**Bands [PRO]** (colour rules for the Engineer-Load view, matching the "red if over-allocated" rule of S3's *Engineer_Load* tab):

| Band | Condition | Colour | Meaning |
|---|---|---|---|
| Under-used | load_vs_planned < 60% | grey/blue | Capacity available for new demand |
| Healthy | 60% ≤ load_vs_planned ≤ 100% | green | On target |
| Stretched | load_vs_planned > 100% and load_vs_avail ≤ 100% | amber | Above target utilisation but within hard capacity |
| **Over-allocated** | load_vs_avail > 100% | red | Plan is impossible without overtime or moves |

Thresholds are settings, not code.

### 8.3 Example from the sample allocation sheet [DER]
- *Mahesh Udar*, October: RN01 80 h + RN04 60 h = **140 h**; planned 136 → 102.9% (Stretched); available 160 → 87.5%. Fine on hard capacity.
- *Deep Thakor* (50% → 80 h available), RN02: **120 h** → 150% of available (Over-allocated).
- *Dharati Patel* and *Twinkle Patel* (50% → 80 h), allocated 100 h each → 125% of available (Over-allocated).
The sample data therefore already contains over-allocations that the system must flag — a useful acceptance test for the load view.

### 8.4 The hours-per-month vs working-calendar inconsistency [DER — OPEN]
- Capacity uses **160 h/month** (≈ 20 weekdays × 8 h, i.e., ~37 h/week).
- The what-if rules use **8 h/day, Monday to Saturday** (48 h/week for one engineer; ~208 h/month). [SRC, S3]
Using both in one system produces contradictions (e.g., a 100%-available engineer working a task shows 48 h/week but is only "available" 37 h/week).

**[PRO] resolution:** capacity is computed from the engineer's **calendar** (actual working days in the period × hours/day × availability %, minus leave and company holidays), with 160 h/month as a *fallback default* when no calendar exists. The scheduling calendar (Mon–Sat, 8 h) is the *default calendar* seeded from S3; leads can switch a team or person to a 5-day calendar. **Decision needed — §30 Q1.**

### 8.5 Weekly and daily views
Weekly capacity = calendar working days in the week × hours/day × availability %. Planned weekly hours = weekly capacity × target utilisation. The Engineer-Load view (§20.6) shows hours per engineer **per week**, since S3 specifies "Hours per engineer per week; red if over-allocated".

### 8.6 What reduces capacity
Availability % (part-time/shared), leave, public holidays (calendar exceptions), training/mentoring time (from the training plan), support/AMC duty, and non-billable allocations. Each is a first-class record so a lead can see *why* an engineer's capacity is lower.

---

### 8.7 Settings: everything is configurable [SRC — owner, 14 Oct; design PRO]
**Model.** `Setting(key, scope_kind, scope_id?, value_json, effective_from, effective_to?, changed_by, reason)`. Scopes cascade **Global → Team → Person → Project → Scenario** (most specific wins). Settings are **effective-dated and versioned**, so a report for August uses August's values, and every computed result (capacity, schedule, scenario, report) stores the **settings snapshot hash** it was computed with. Changing a setting never silently rewrites history; a *recalculate* action applies new values forward or, explicitly, to a chosen period.

| Area | Configurable items (non-exhaustive) | Seed default |
|---|---|---|
| **Time & hours** | Standard hours per month; hours per day; working days of the week; week start day; per-person and per-team **calendars**; holidays and exceptions; whether capacity is *calendar-derived* or *fixed hours/month*; rounding of hours and days; time zone and date/number formats | 160 h/month; 8 h; Mon–Sat; calendar-derived |
| **Utilisation** | Target utilisation (global, team, person); whether *Proposed* allocations count toward load; context-switch penalty per extra concurrent project | 85%; no; 0% |
| **Load bands** | Thresholds and colours for under-used / healthy / stretched / over-allocated | 60% / 100% / 100% of available |
| **Skills** | Skill catalogue; **number of levels and their names** (not fixed at 5); thresholds for *resource* and *deployable*; required % per skill per team; staleness period; evidence-vs-rating disagreement threshold | 5 levels; ≥ 2; ≥ 3; 12 months; 1.5 |
| **Training** | Knowledge-band cut-offs and labels; module catalogue; module→skill weights; doubt ageing limits | 80 / 60 / 40 % |
| **Scheduling** | Working calendars; link-lag calendar policy; task type default (fixed work / fixed duration / fixed units); whole-day rule; priority rule for sequencing; productivity-factor profile (Compat / Realistic / custom tables); ramp-up curve; default placement rule for what-if joins | Compat; whole-day on |
| **What-if** | Which rules apply (e.g., "lost-all-engineers", skill gates, replacement ramp); candidate-ranking weights (§9.7); approval rules (which changes require leadership) | As §15 |
| **Project health** | Thresholds for schedule variance, delay ratio, progress gap; status vocabularies and colours | §11.6 |
| **Pipeline** | Stage list; default win probability per stage; weighting method | as sample |
| **Alerts** | Each rule's threshold, severity, channel, snooze | §22 |
| **Infrastructure** | Disk-free thresholds; stale-VM days; backup freshness | 10%; 30 d |
| **Vocabularies** | Every enumeration in Appendix A is an editable list (values, order, colour, active flag) | Appendix A |
| **Custom fields** | User-defined fields on Person, Project, Task, Host, Demand… (type: text, number, date, choice, person), shown in lists, filters, reports, API and CLI — the V1 *custom employee columns* feature generalised | none |
| **Reports & dashboards** | Report parameters and saved views; dashboard layout and widget set | §20–21 |
| **Platform** | Backup schedule and retention; token lifetimes; import sample-space behaviour | §25 |
Settings are editable in the UI (*Settings* workspace), the CLI (`eos settings get|set|history --scope team:LPP`), the API and MCP; every change is audited with a reason.

---

## 9. Skills

### 9.1 Skill catalogue [SRC — S2 Skills sheet; S7]
| ID | Skill | Category | Description |
|---|---|---|---|
| SK01 | SQL | Database | Queries, stored procedures, DB design |
| SK02 | DOW | Platform | DOW configuration and usage |
| SK03 | Historian | Data / Historian | Historian setup, tags, data collection |
| SK04 | Asset Structure | Platform | Asset model / hierarchy design |
| SK05 | General XStudio | Platform | General XStudio development |
| SK06 | Dashboards | Visualisation | Dashboard design and build |
| SK07 | Reports | Visualisation | Report design and build |
| SK08 | Custom Pages (HTML, CSS and JS) | Web Development | Custom web pages and scripting |
| SK09 | Server Management | Infrastructure | Server install, config, maintenance |
| SK10 | Networking | Infrastructure | Network setup and troubleshooting |

"Add new skills at the bottom; also add a matching column in Skill_Matrix" (S2) — in V2 adding a skill is a single operation that creates the catalogue row and the empty matrix cells.

### 9.2 Rating scale [SRC S2; descriptions 2–4 are proposed wording per the source's own note]
| Rating | Level | Meaning |
|---|---|---|
| 1 | No knowledge | Has not worked on it |
| 2 | Basic | Understands concepts, needs guidance |
| 3 | Intermediate | Works independently on standard tasks |
| 4 | Advanced | Handles complex work, can guide others |
| 5 | Expert | Go-to person, designs solutions |

### 9.3 Thresholds [SRC S2 Definitions]
- **Resource** for a skill: rating ≥ **2** ("rating above 1 = has some knowledge").
- **Deployable** for a skill: rating ≥ **3** ("can work independently").
Both are global settings.

### 9.4 Rating formulae
**9.4.1 Existing aggregates [SRC S2 Skill_Matrix / Skill_Summary]**
```
Avg Rating (engineer)            = AVERAGE(ratings across skills)
Skills at Deployable Level (eng) = COUNTIF(ratings, ">=3")
Resources (skill, team)          = COUNTIFS(team, rating >= 2)
Deployable (skill, team)         = COUNTIFS(team, rating >= 3)
Competency % (skill, team)       = AVERAGEIFS(rating) / 5
Skill Set Required %             = (input; currently blank)
Gap vs Required                  = Competency % − Required %         (blank when Required is blank)
```
**9.4.2 Current state of the numbers [DER — all teams combined, 23 rated engineers]**

| Skill | Resources (≥2) | Deployable (≥3) | Competency % |
|---|---|---|---|
| SQL | 20 | 8 | 51.3% |
| DOW | 18 | 12 | 48.7% |
| Historian | 18 | **4** | 43.5% |
| Asset Structure | 10 | 5 | 36.5% |
| General XStudio | 18 | 9 | 49.6% |
| Dashboards | 21 | 9 | 50.4% |
| Reports | 16 | 13 | 54.8% |
| Custom Pages (HTML, CSS, JS) | 8 | **4** | 33.0% |
| Server Management | 8 | **3** | 32.2% |
| Networking | 8 | 5 | 31.3% |

**[VER]** SQL: sum of 23 ratings = 59; 59 ÷ 23 ÷ 5 = 51.3% ✓. LPP-Engineering SQL: 36 ÷ 13 ÷ 5 = 55.4% ✓.

**Bus-factor reading [DER]:** Server Management has **3** deployable engineers; Historian and Custom Pages **4**; Networking and Asset Structure **5**. These are the skills where one leave or one move creates a gap — and the sample LOG-001 already records exactly such a gap (Server Management level 4 needed, none free).

**9.4.3 Evidence score (advisory only) [PRO — answers note N3]**
Lead assessment alone drifts. Training data (S6) gives per-module evidence. The formulae below compute an **evidence-suggested rating shown beside the manual rating**; the rating *used* for gating, ranking and reports is the **manual** one (§9.5) unless the owner explicitly chooses the *blend* mode in settings. Proposed:
```
E(p,s) = Σ_m w(m,s) · K(p,m) / Σ_m w(m,s)             # evidence score 0..1
K(p,m) = 1 if Know, 0 otherwise                          # consistent with S6 "Know %" = Know ÷ Total modules
R_evidence(p,s) = 1 + 4 · E(p,s)                          # maps 0..1 to 1..5
R(p,s)  = α · R_lead(p,s) + (1 − α) · R_evidence(p,s)     # α default 0.5; α = 1 if no module mapping exists
Rating used for gating = the manual rating (default), or round-half-up(R) only if the setting "use blended rating" is on
```
`w(m,s)` is the module→skill weight (§10.2). A rating older than 12 months, or with fewer than 5 mapped modules, is flagged **stale/thin**.

**9.4.4 Why a blend [DER]:** the two sources disagree for some people. Example: Abhinath Maurya's average skill rating is 2.2 (S2/S7) but he knows 116 of 132 modules (87.9%, S6); Chirag Dhimmar is rated 5 in SQL, Historian, General XStudio and Reports yet knows 62.9% of modules. A disagreement above 1.5 levels is a **review flag**, not a silent average.

### 9.5 Skill transactions and progression [SRC N12; **manual progression — owner decision, 14 Oct**]
**Rule: skill ratings and levels are changed only by a person, never by the system.** The owner (and, later, team leaders) sets a rating through an explicit action that records *who, when, old value, new value, reason/evidence note*. No formula, import, training event or task completion alters a rating automatically.

**What the system does instead:**
- **Records** every manual change as a `SkillTransaction` (kind `assessment` or `manual_adjustment`), so history is complete and any past date can be reconstructed.
- **Shows evidence** next to the rating, to inform the human decision: module Know % for the skill (§9.4.3), task-hours worked on the skill, mentoring given/received, recent review notes, doubts raised/solved, and rating age.
- **Suggests (optionally):** a *"consider review"* hint when evidence and rating disagree (A16), when a rating is stale (A15), or when an engineer's evidence crosses a **configurable** threshold. A suggestion is a to-do item for a human; accepting it still requires the human to set the value.
- **Never demotes automatically.** Staleness only flags.

**Transaction kinds:** `assessment` (initial or periodic rating), `manual_adjustment` (change with mandatory reason), `evidence_note` (attach evidence without changing the rating), `review_requested`, `review_dismissed`. Training and task events are logged separately as training/work records and merely *referenced* as evidence.

**Progression criteria are optional guidance, not rules.** Teams may define *guidance checklists* per level step (e.g., "≥ 55% modules known; 80 task-hours under guidance") as free text or measurable hints shown on the review screen. The Teams chat of 26 Sep confirms the progression method was still to be decided; the owner has now decided it is a manual judgement. All level definitions and the number of levels are settings (§8.7).

### 9.6 How skill factors into assignment and movement [SRC N4, N17; model PRO]
Skill acts in **three** distinct ways. The system must keep them separate:

1. **Gate (eligibility).** A task or demand has a *minimum level* `q` for each required skill. An engineer is **eligible** if `rating ≥ q`. Below that the engineer is **ineligible by default**; a lead may override with a mentor pairing (§9.8). *Example from S3:* Project 2 needs SQL ≥ 3 → Priyanka Makwana (SQL 2) is "Not eligible (skill)".
2. **Rank (fit).** Among eligible engineers, order candidates (§9.7).
3. **Factor (productivity).** Work done by an engineer on a task is scaled by a factor (note N17):
   `work_done_hours = calendar_working_hours_since_start × allocation% × factor(p, task)`
   where `factor = skill_factor × experience_factor`.

**Factor profiles [PRO]:**
| Profile | Skill factor | Experience factor | Use |
|---|---|---|---|
| **Compat** (default) | 1.0 for every eligible engineer | 1.0 (no ramp-up) | **Reproduces S3 exactly** ("new joiners work at full speed") — needed so the verified examples in §15.8 stay valid |
| **Realistic** | rating ≥ q → 1.00; rating = q−1 (only if override) → 0.60; rating ≤ q−2 → ineligible | Ramp-up after a move: week 1 → 0.50, week 2 → 0.80, thereafter 1.00; first time on this product line → ×0.90 for the first 4 weeks | What-if with realism; S3 explicitly names an 80% ramp-up for two weeks as a later addition |
Both tables are editable settings and are recorded on every scenario result so a number can always be traced to its profile.

### 9.7 Multiple engineers with the same skill level [SRC N7; method PRO]
When several eligible engineers tie on rating, rank with a lexicographic then weighted rule, always explained to the user:

1. **Hard gates** (must pass): eligibility, availability in the window, location compatibility (Office/Site), not on leave.
2. **Least-sufficient skill first:** prefer the engineer whose rating is closest to (but ≥) the required level, so an expert is not consumed by routine work (protects scarce level-5s).
3. **Cost of leaving:** the slip the move causes the source project (from the scenario engine). Lower is better.
4. **Float at source:** engineers whose current tasks have the most slack can move earliest at no cost (S3 Scenario 2: "Twinkle can move immediately at no cost").
5. **Continuity:** already on the receiving project/product.
6. **Development opportunity:** prefer a learner when the task has float (stretch assignments), tied to a training plan.
7. **Load balance:** lowest current utilisation (§8.2).
8. **Stable tie-break:** earliest free date, then name.
A weighted score `Σ w_i · normalised_i` produces the displayed order; weights are settings. The UI shows the **top N with the reasons** and never auto-assigns — a human decides.

### 9.8 Interconnecting skills and scenarios [SRC N14; PRO]
- **Skills → scenarios:** eligibility gates, factors (§9.6), candidate ranking (§9.7), and "move a skill" scenarios (§15.2).
- **Scenarios → skills:** an approved move creates `task_evidence`/experience transactions after the work is done; a mentor pairing creates a *mentoring* training transaction and **reduces the mentor's capacity** by the agreed hours (S2's LOG-001: pair Abhinath Maurya (Server Management rating 2) with Sagar Rana (rating 5) as mentor for 5 h/week).
- **Gaps → training:** a Demand with no eligible engineer generates a *training-need* suggestion ("N engineers are one level short") that feeds the training plan (§10.7).

### 9.9 Skill-landscape analytics [PRO]
Per skill and team: resources, deployable, competency %, **required %** (new `RequiredSkillLevel`), gap, bus-factor (deployable count), concentration (share of deployable hours held by the top engineer), forward **demand vs supply** by month (§12.5), and "who is one level away" lists.

---

## 10. Training

### 10.1 Source structure [SRC — S6, 132 module rows]
| Product | Module groups (sub-module count) | Rows |
|---|---|---|
| **X-Force Framework** | User(1), Role(1), Navigation(2), System(1), Application(1), Entity(5), Attributes(6), LV(13), SP to LV(1), Edit(13), Trigger(1), Inline Grid(2), Tab Container(1), HTML Control(1), GIS Control(2), Pages(5), Work Flow(7), Schedule(3), Report(4), SMTP(2), Standard Dashboard(9), Dashboard(2) | 83 |
| **X-Force Historian** | What is Historian(1), Collector(4), Channel(2), Tag(6), Storage(2), Management Console(1), History data(1), History Trend(1), Live Data(1), Live Trend(1), Save data(1), Create Excel Report(1), Schedule Report(1), Asset Structure(7), Summary Services(2), Sync Services(2) | 34 |
| **X-Force MES** | API Configuration(1), API Configuration in Edit/LV/Workflow(1), Event(1) | 3 |
| **X-Force DOW** | DOW(1), Asset Structure(10), Control Room Logbook(1) | 12 |
| **Total** | | **132** [VER: 83+34+3+12] |
(The source contains spelling errors — "Navigaction", "Collertor", "Chanal", "Assect/Asect Structure", "Contro;l" — which the importer normalises while keeping the original text as an alias.)

### 10.2 Module → skill mapping [PRO]
S6 modules (fine) and S2 skills (coarse) are never linked in the source. EOS V2 adds `ModuleSkillMap(module, skill, weight)` so evidence rolls up:

| Skill | Candidate modules (indicative; weights to be confirmed by leads) |
|---|---|
| DOW | Product *X-Force DOW* (all 12), Pages, Standard Dashboard, Control Room Logbook |
| Historian | Product *X-Force Historian* (34 rows) |
| Asset Structure | *Historian → Asset Structure(7)* and *DOW → Asset Structure(10)* |
| General XStudio | Framework: User, Role, Navigation, System, Application, Entity, Attributes, Edit, Trigger, Inline Grid, Tab Container, Work Flow |
| SQL | LV(13), SP to LV, Attributes (calculated columns), Edit (before-save logic) |
| Dashboards | Standard Dashboard(9), Dashboard(2) |
| Reports | Report(4), Schedule(3), Historian → Create Excel Report / Schedule Report |
| Custom Pages | HTML Control, GIS Control, Pages |
| Server Management / Networking | no modules exist today → **training-content gap** (see §10.8) |

### 10.3 Knowledge states and Know % [SRC — S6 Master/Module sheets]
- Cell values: **Know**, **don't Know**, **Need to discuss**. Counts across the 132 × 27 matrix: Know **2,022**, don't Know **1,502**, Need to discuss **40** (total 3,564 = 132 × 27 ✓ **[VER]**).
- **Know % (engineer)** = Know ÷ Total modules (132). *Example:* Ayush 71 ÷ 132 = 53.8% ✓.
- Organisation-wide Know % = 2,022 ÷ 3,564 = **56.7%** [DER].
- A separate sheet, **Module Proficiency Level**, is meant to hold a named level per engineer per module; today it contains **3,456 "Beginner"** and **2 "Expert"** — i.e., it was initialised with defaults and has not been filled in. [DER] Intermediate/Advanced names are undefined. **[OPEN Q4]**

### 10.4 Engineer knowledge snapshot [DER from S6 Master, sorted by Know %]
| Rank | Engineer | Know | Don't know | Need to discuss | Know % |
|---|---|---|---|---|---|
| 1 | Sagar | 129 | 3 | 0 | 97.7% |
| 2 | Mahesh | 120 | 12 | 0 | 90.9% |
| 3 | Abhinath | 116 | 16 | 0 | 87.9% |
| 4 | Lalji | 110 | 22 | 0 | 83.3% |
| 5 | Dharati | 100 | 32 | 0 | 75.8% |
| 6 | Priyanka | 98 | 33 | 1 | 74.2% |
| 7 | Twinkle | 92 | 40 | 0 | 69.7% |
| 8 | Malek | 91 | 38 | 3 | 68.9% |
| 9 | Karan | 90 | 39 | 3 | 68.2% |
| 10 | Krishna | 88 | 44 | 0 | 66.7% |
| 11 | Deepak | 86 | 28 | 18 | 65.2% |
| 12 | Chirag | 83 | 46 | 3 | 62.9% |
| 13 | Dharmesh | 81 | 49 | 2 | 61.4% |
| 13 | Safwan | 81 | 51 | 0 | 61.4% |
| 15 | Ziual | 80 | 52 | 0 | 60.6% |
| 16 | Deep | 72 | 60 | 0 | 54.5% |
| 17 | Ayush | 71 | 59 | 2 | 53.8% |
| 17 | Devanshi | 71 | 58 | 3 | 53.8% |
| 19 | Utsav | 67 | 64 | 1 | 50.8% |
| 20 | Dev | 66 | 65 | 1 | 50.0% |
| 21 | Tejas Kale | 64 | 68 | 0 | 48.5% |
| 22 | Kartik | 47 | 82 | 3 | 35.6% |
| 23 | Tejas Khadke | 38 | 94 | 0 | 28.8% |
| 24 | Priyen | 36 | 96 | 0 | 27.3% |
| 25 | Dharmendra | 25 | 107 | 0 | 18.9% |
| 26 | Krinav | 10 | 122 | 0 | 7.6% |
(The *Module* sheet has a 27th engineer column, *Udaykumar*; his summary row was not in the range read. Names are as spelled in S6; mapping to Person records is via the alias screen, §6.4.)

### 10.5 Levels and manager actions [SRC S6 Dashboard — with an inconsistency]
| Dashboard level | Know % | Manager action (Dashboard) |
|---|---|---|
| Good | ≥ 80% | — |
| Medium | 60–79% | OK |
| Weak | 40–59% | Monitor / mentor |
| Critical | < 40% | (priority list; "Priority 1..5") |
The *Master* sheet uses a different vocabulary for the same bands (**Fast** ≥ 80%, **Good** 60–79%, **Medium** 40–59%, **Slow** < 40%). **[OPEN Q5]** one vocabulary must be chosen; V2 stores the **percentage** and derives the label from a configurable table so the label can change without rewriting data.

### 10.6 Doubts and the training ledger [SRC S6 "Need to Discuss"; N13]
**Doubt lifecycle:** *Raised* (module, sub-module, detail, date, raising engineer) → *Assigned* (mentor, date) → *Solved* (date, result, remarks) or *Hold*. Result values in the source: **Solved, Assign, Hold** (blank = not yet assigned).
- Observed log [DER]: 15 logged doubts — **8 Solved, 2 Assign, 1 Hold, 4 unassigned**; topics: LV (5), Work Flow (4), Entity (2), Asset Structure, Edit, SP to LV, Attributes (1 each). **Chirag is the assigned mentor on 9 of the 11 assigned doubts** — a single-mentor bottleneck.
- The dashboard header reports **71 total doubts, 8 solved, 63 open, 60 need action**; the log lists only 15. The extra are presumably the *Need to discuss* module cells plus logged items. **[OPEN Q6]**: define whether a "Need to discuss" cell *is* a doubt.
- Remarks show mentoring practice, e.g., "Need to practice before work on this; understand SQL JOIN…" — free text that V2 keeps and links to the module.

**Training transaction kinds [PRO]:** `learn` (module state change to Know, with date and trainer), `session` (group session with attendees, hours), `assessment`, `mentoring` (pair, hours/week), `doubt_raised`, `doubt_solved`, `shadowing` (engineer shadows another on a live task). Every transaction updates `PersonModule` and, via §9.4.3, feeds skill evidence.

### 10.7 Training plans [PRO]
A plan is a goal for a person: *reach module state / skill level X by date D with mentor M*. Plans are generated from: gaps in the demand model (§12), the lowest-Know-% engineers, and upcoming pipeline skills (§12.4). Plan hours are subtracted from capacity (§8.6). Progress is read from transactions — no separate manual percentage.

### 10.8 Training-content gaps [DER]
- *Server Management* and *Networking* skills have **no modules** in S6, yet they are the two thinnest skills in S2 (3 and 5 deployable). EOS V2 should prompt leads to author modules for them.
- Product coverage skews to the X-Force Framework (83 of 132 rows).
- Historian modules (34 rows) correspond to a skill (Historian) with only 4 deployable engineers.

### 10.9 Adding a module (replaces the 13-step Excel procedure) [SRC S6 "Guide to add Module"]
The workbook requires editing named ranges and dependent dropdowns across three sheets. In V2: *Add module* = one form (product, module, sub-module, skill mapping). Dropdowns are generated from data; no named-range maintenance exists.

---

## 11. Projects: portfolio, live delivery, status and health

### 11.1 Two project populations [DER]
| Population | Source | Count | Detail available |
|---|---|---|---|
| **Portfolio** (ERP master) | S9 `ProjectList` | 454 rows | Commercial and status data only |
| **Live LPP delivery** | S8 `LPP` sheet + per-project trackers | 11 rows (10 active + 1 newly entered) | Task-level plans, owners, progress |
EOS V2 holds **both** under one `Project` entity: the portfolio rows provide identity and commercials; the live rows additionally have plans, assignments and health.

### 11.2 Portfolio statistics [DER — S9]
| ERP status | Count | | Project type | Count |
|---|---|---|---|---|
| CLS | 192 | | Project and Project Services | 412 |
| RLS | 88 | | Service+Spare | 34 |
| TECO | 64 | | Mandays | 6 |
| FUTURE ORDER | 62 | | Runrate | 2 |
| HLD | 48 | | | |
Interpretation of codes (likely SAP statuses: *CLS* closed, *RLS* released/active, *TECO* technically completed, *HLD* on hold) is **[OPEN Q7]**. Variant prefixes `W_` (warranty/"future order" shadow) and `V_` appear in project numbers (e.g., `W_PRO2606016_1` shadows `PRO2606016`); V2 links them via `parent_project_id` so the same job is not double-counted.

**Portfolio columns available [SRC S9]:** Project No, IKOM, Oppnumber, Order No, Project Type, Project Name, Order Amount, OIGP, OIGM, PO Start/End Date, Main Status, Project Manager, Technical Manager, Delivery Leader, Account, Area, Business Category, Vertical, order-form count, invoice count and amounts (basic, total, received, due), and more (46 columns). The OIGM column contains HTML fragments (`<p><span…>52.57%</span></p>`) from the export; the importer strips markup.

### 11.3 Live delivery projects [SRC — S8 "LPP" sheet, as of the tracker date]
| # | Project No | Customer / name | Product | Status | Technical manager | PM |
|---|---|---|---|---|---|---|
| 1 | PRO2512003 | NTPC Korba — Calibration Management System | CMS | Deployed | Sagar Rana | Brijesh Vasoya |
| 2 | V_PRO2602059_2 | Petronet LNG — E-Logbook | DOW | Under Development | Sagar Rana | Chirag Koladiya |
| 3 | PRO2601010 | DCM Shriram, Jhagadia — E-Logbook | DOW | Under Development | Sagar Rana | Brijesh Vasoya |
| 4 | PRO2607048 | Dr. Reddy's Labs CTO-6, "Viza" (as written in the source) — PIP | Historian | Under Development | Snehil B | Vivek Deshani |
| 5 | PRO2412090 | Emami Agrotech, Haldia — LIMS | LIMS | Under Development | Sagar Rana | Brijesh Vasoya |
| 6 | PRO2602018 | LMEL — Networking and X-MES, 1.2 MT integrated steel plant (Ghugus); 1-year subscription Feb 2026–Feb 2027 | MES | Under Development | Chirag Dhimmar | Chirag Koladiya |
| 7 | PRO2605051 | Pidilite (Amod) — Automatic Batch Process Monitoring PIP | MES | Under Development | Chirag Dhimmar | Vivek Deshani |
| 8 | PRO2503040 | Sohar Steel, Oman — Historian and Dashboard | MES | Under Development | Sagar Rana | Vivek Deshani |
| 9 | PRO2502024 | BANCO — Dashboard and screen design | MES | **Hold** | Chirag Dhimmar | Chirag Koladiya |
| 10 | PRO2603013 | Emami Agrotech — Line integration and data centralisation | MES | Under Development | Prithwesh Vyas | Chirag Koladiya |
| 11 | PRO2608090 | Pidilite Tennax — Electronics batch (name truncated in the extract) | BATCH | **Entered** | Chirag Dhimmar | Vivek Deshani |

**Status vocabulary [SRC S8 Master]:** Entered, Under Development, Hold, Deployed, Completed.

### 11.4 Project tracker anatomy [SRC — S8 per-project sheets]
Header block per project: *Project Name, Project Number, Project Manager, Technical Lead, Team/Engineers (initials), Planned Completion*, and KPIs *Overall Progress %, Project Health, Tasks Completed (x / y), Delayed Tasks, Days to Planned Completion, Last Updated*.
Task table columns: *Sr, WBS, Module, Task Name, Start Date, End Date, Duration (Days), Progress %, Status, Priority, Owner, Remaining Days* (the LMEL sheet uses *Engineer* instead of *Owner*). To the right is a **day-by-day Gantt strip** (one column per calendar day, cell-coloured by *Completed / In Progress / Pending-Future / Delayed*, with a *Today* marker). The V2 combined Gantt (§16) replaces the manually coloured strip with a computed one.

Task status vocabulary: **Completed, In Progress, Pending/Future, Delayed**.

### 11.5 Snapshot as of 17 Sep 2026 [SRC S8; days check = VER]
| Project | Progress | Health | Done / total tasks | Delayed | Planned completion | Days to planned |
|---|---|---|---|---|---|---|
| NTPC Korba CMS | 81% | On Track | 9 / 13 | 3 | 14 Jun 2026 | −95 [VER] |
| DCM Shriram e-Logbook | 70% | Moderate Risk | 10 / 15 | 3 | 26 Mar 2026 | −175 |
| Sohar Steel MES | 62% | Moderate Risk | 18 / 33 | **12** | 15 Sep 2026 | −2 |
| Petronet LNG e-Logbook | 38% | **At Risk** | 11 / 47 | **19** | 31 Dec 2026 | +105 [VER] |
| Emami LIMS | 52% | Moderate Risk | 3 / 10 | 7 | 31 Jul 2025 | −413 |
Petronet also tracks "Logbook development completed" as 200 / 285 (a domain-specific KPI; V2 supports project-level custom KPIs).

**Observations [DER]:** health labels are hand-entered and not obviously consistent with the numbers (Korba is *On Track* with 3 delayed tasks and a planned date 95 days past; DCM has 175 days past plan). Health must be **computed** (§11.6), with a manual override that is recorded.

### 11.6 Computed health [PRO]
```
schedule_variance_d = forecast_finish − planned_finish                 (working days; + = late)
delay_ratio         = delayed_tasks / open_tasks
progress_gap        = expected_progress(today) − actual_progress        (pp)
health = AtRisk        if schedule_variance_d > 20  or delay_ratio > 0.40  or progress_gap > 25pp
         ModerateRisk  if schedule_variance_d > 5   or delay_ratio > 0.15  or progress_gap > 10pp
         OnTrack       otherwise
```
All thresholds are settings; **expected_progress** comes from the baseline plan; **forecast_finish** from the scheduler (§14). A lead may override health with a reason; overrides expire at the next status update.

### 11.7 Discrepancies between sources [DER]
| Item | S8 (tracker) | S9 (project review) |
|---|---|---|
| Sohar Steel planned completion | 15 Sep 2026 | 25 Feb 2026 (contractual) |
| Sohar Steel technical manager | Sagar Rana | Snehil Bhadkamkar |
| Pidilite technical manager | Chirag Dhimmar | Snehil Bhadkamkar |
Resolution [PRO]: keep both dates (*contractual* vs *planned/forecast*) as separate fields, and keep the *technical manager of record* per source with a flag when they disagree.

### 11.8 AMC and support tickets [SRC — S8 "AMC"]
Columns: *P. No, Project Name, Product Name, In AMC (Y/N), Raised Date, Raised By, Report Issue, Assign Date, Assign By, Resolve Date, Assigned Engineer(s), Root Cause, Notes/Remarks, Report Flow / Applicable Units, Reference / Follow-up, Status*. Examples show typical issues (report data not showing — a database connectivity problem between two systems; e-logbook report generation delayed 1–2 hours — slow report-generation execution). V2 treats tickets as **skill-matched work items** (§13.6) and reports time-to-assign, time-to-resolve, recurring root causes and per-engineer support load, which also reduces that engineer's capacity (§8.6).

### 11.9 Pipeline / opportunities [SRC — S2 Upcoming_Projects; see §12.4]

---

## 12. Demand

### 12.1 Demand lines [SRC — S2 Project_Demands]
Columns: *Demand ID, Project ID, Project Name, Skill Required, Min Level (1–5), Hrs / Week, Start Date, End Date, Location, Eligible Engineers (≥ level), Best-rated Engineer, Demand Status*. One line per skill per project.

**Formulae [SRC]:**
- *Eligible Engineers* = count of engineers whose rating for the skill ≥ Min Level.
- *Best-rated Engineer* = engineer with the highest rating on that skill (lookup).
**Status values [SRC]:** Filled, Partially Filled, Gap – Escalated, Open.

### 12.2 Computed fill status [PRO]
```
required_h(w)  = hours_per_week of the demand line in week w
supplied_h(w)  = Σ allocations (Confirmed) to this demand in week w
fill(w)        = supplied_h / required_h
status = Filled          if fill ≥ 1 for every week in [start, end]
         Partially Filled if 0 < fill < 1 in some week
         Open            if no allocation
         Gap – Escalated  if a Change/Gap log entry of kind "Capacity Gap" exists and is not closed
```
Allocations also store **who** and whether they are *Proposed* (what-if) or *Confirmed*.

### 12.3 Sample demand lines [SRC, sample data]
| ID | Project | Skill | Min | h/wk | Window | Location | Eligible | Best | Status |
|---|---|---|---|---|---|---|---|---|---|
| DM01 | RN01 Historian Upgrade | Historian | 3 | 30 | Sep–Nov 2026 | Office | 4 | Chirag Dhimmar | Filled |
| DM02 | RN01 | Dashboards | 3 | 20 | Sep 2026–Jan 2027 | Office | 9 | Chirag Dhimmar | Filled |
| DM03 | RN02 Production Dashboard Rollout | Dashboards | 4 | 25 | Oct 2026–Feb 2027 | Office | 5 | Chirag Dhimmar | Filled |
| DM04 | RN03 Plant Reporting Suite | Reports | 3 | 20 | Sep–Nov 2026 | Office | 13 | Chirag Dhimmar | Filled |
| DM05 | RN04 Asset Model Build | Asset Structure | 4 | 10 | Oct 2026–Feb 2027 | Site | 3 | Mahesh Udar | Partially Filled |
| DM06 | RN05 Server Migration | Server Management | 4 | 25 | Oct 2026–Jan 2027 | Site | 2 | Sagar Rana | Gap – Escalated |
| DM07 | UP01 Project A | General XStudio | 3 | 40 | Sep–Oct 2026 | Office | 9 | Chirag Dhimmar | Open |
| DM08 | UP02 Project B | Asset Structure | 4 | 20 | Nov 2026–Apr 2027 | Office | 3 | Mahesh Udar | Open |
| DM09 | UP04 Project D | Custom Pages | 4 | 30 | Sep–Nov 2026 | Office | 2 | Dharati Patel | Open |
| DM10 | UP04 Project D | Reports | 4 | 40 | Sep–Nov 2026 | Office | 8 | Chirag Dhimmar | Open |
**Observation [DER]:** "Best-rated engineer" is Chirag Dhimmar for 5 of 10 lines — if every line takes the best-rated engineer he is over-committed. The system must **not** equate "best-rated" with "assigned"; ranking (§9.7) accounts for load.

### 12.3.1 The Change/Gap log entry in the sample [SRC]
`LOG-001` — *Capacity Gap*, project RN05, demand DM06, raised by Dharati Patel on 10 Sep 2026: "No engineer at Server Management level 4 free from Oct"; gap type *Proficiency mismatch*; impact 400 h; option/decision: "Pair Abhinath Maurya (level 2) with Sagar Rana as mentor 5 hrs/week"; approver Leadership; decision date 15 Sep 2026; status Approved. This is the canonical shape of a gap → option → decision record (§17.4).

### 12.4 Pipeline (upcoming projects) [SRC — S2 Upcoming_Projects]
Columns beyond the demand-like ones: *Expected Order Value (lakh), Expected HOURS, Likely Booking Month, Duration (months), monthly hours (Sep 2026 → …), Total hours (sum), Check, Status (Expected), Stage, Win Probability, Required skills, Weighted hours*.
| ID | Name | Hours | Booking | Months | Stage | Win % | Skills | Weighted hours |
|---|---|---|---|---|---|---|---|---|
| UP01 | Project A | 704 | Oct 2026 | 2 | Proposal Sent | 70% | General XStudio, SQL | 492.8 |
| UP02 | Project B | 1,188 | Oct 2026 | 6 | Negotiation | 60% | Asset Structure, Server Management | 712.8 |
| UP03 | Project C | — | Oct 2026 | — | Qualified | 40% | Historian, Dashboards | 0 |
| UP04 | Project D | 1,584 | Sep 2026 | 3 | Proposal Sent | 80% | Reports, Custom Pages | 1,267.2 |
**[VER]** Weighted hours = hours × win probability: 704 × 0.7 = 492.8 ✓; 1,188 × 0.6 = 712.8 ✓; 1,584 × 0.8 = 1,267.2 ✓. Monthly distribution is hours ÷ duration (704/2 = 352; 1,188/6 = 198; 1,584/3 = 528 ✓).
Win probability by **stage** defaults [PRO]: Qualified 40%, Proposal Sent 70%, Negotiation 60%…80% (the sample is not monotonic; V2 sets probability per opportunity and warns on non-monotonic stage defaults). Won opportunities convert to projects, carrying their demand lines.

### 12.5 Forward supply–demand analysis [PRO — answers N6, §12 "Demand vs Supply"]
For each month *m* and skill *s*:
```
demand_h(s,m) = Σ over live projects (confirmed demand) + Σ over opportunities (weighted hours share for s)
supply_h(s,m) = Σ over deployable engineers e of free_planned(e,m)      # pooled free hours (an engineer's free hours are counted once per skill, shown as "pooled" to flag overlap)
gap_h(s,m)    = demand_h − supply_h
```
Outputs: a heat map (skills × months), an **early-warning list** ("gap in Asset Structure in Nov: 124 h; 3 eligible engineers"), and for every gap a **suggested options list** (re-time, re-assign, train, pair, hire) — never "hire" as the only option (principle §1.2).

---

## 13. Allocation

### 13.1 Allocation records [SRC — S2 Resource_Allocation]
Columns: *Engineer, Team, Project ID, Demand ID, Role on Project, Allocation Status, monthly planned hours (Sep 2026 → …)*. **Status values:** *Confirmed*, *Proposed*. "Proposed rows hold what-if" (S1 workbook table).
Sample (10 rows): Mahesh Udar (Lead, RN01, 80 h/mo), Lalji Bodar (RN01, 120), Sagar Rana (Lead, RN02, 100), Deep Thakor (RN02, 120), Chirag Dhimmar (Lead, RN03, 120), Priyanka Makwana (RN03, 120), Mahesh Udar (RN04, 60), Twinkle Patel (RN04, 100), Dharati Patel (Lead, RN05, 100), Abhinath Maurya (RN05, 120), Malek Mohammed Hasnain (UP04, **Proposed**, 160).

### 13.2 Two granularities [DER]
| Granularity | Where | Use |
|---|---|---|
| **Monthly hours** per engineer × project | Resource_Allocation (S2) | Capacity planning, pipeline, reporting |
| **Task assignments** (hours, dates) | Project plans (S3, S8) | Scheduling and what-if |
**[PRO]** Both coexist. When a project has a plan, monthly allocation is **derived** from task assignments (and shown as *derived*); otherwise the manual monthly numbers are used. A reconciliation report flags projects where plan-derived and manual hours differ by more than a tolerance.

### 13.3 Parallel projects [SRC N5]
An engineer may hold several simultaneous allocations. Rules [PRO]:
1. Σ allocations in a month ≤ available hours (hard warning if exceeded), ≤ planned hours (soft).
2. Per-day task assignment percentages across concurrently scheduled tasks may not exceed 100% (the scheduler flags over-assignment exactly as MS Project does).
3. **Context-switch overhead (optional setting):** with more than 2 concurrent projects, reduce effective hours by a configurable % per extra project (default 0%, so S3 numbers hold).
4. Part-time engineers (availability < 100%) show their reduced ceiling directly.

### 13.4 Assigning an engineer to a demand [PRO]
Candidate list = engineers passing the gates (§9.7 step 1) ordered by the score. The user can accept, adjust hours, or choose "Propose" (writes a *Proposed* allocation that appears in what-if comparisons but not in committed capacity). Confirming converts it to *Confirmed* and writes a `ResourceMovement`/allocation transaction.

### 13.5 Releasing capacity
When a task completes or a project ends, allocations are *Released* from that date. The released hours become **free capacity** immediately visible in the supply view. The **earliest-release date** per engineer (the "last day on current work" in S3 Scenario 2) is computed from the scheduler, not typed.

### 13.6 Work after completion: skill-matched work items [SRC S5 26 Sep; PRO]
Principle: a finished task is **not owned** by its original engineer. When an issue appears (defect, AMC ticket, rework):
1. **Triage** sets *required skill(s), minimum proficiency, urgency, estimated hours* (difficulty to diagnose ⇒ proficiency needed).
2. **Candidates** = eligible engineers (gate), ordered by §9.7 with **continuity** (original engineer) as a ranking factor but not a right.
3. **Availability** is then checked; if the original engineer "CAN do it" and is available, they are preferred; otherwise the next eligible engineer is proposed.
4. The assignment is a normal allocation/transaction, so its cost appears in the **source** of the capacity (the engineer's own current project slips by what the interruption costs — computed via the scenario engine, which gives the lead the real price of "pulling Person A back").

---

## 14. The scheduling engine (MS Project semantics)

### 14.1 Concepts [SRC — S3]
| Concept | Meaning |
|---|---|
| **Task** | A piece of work in a project, e.g., "Historian configuration" |
| **Work** | Total hours the task needs, e.g., 192 h |
| **Assignment** | The engineers working the task (100% = 8 hours per day each) |
| **Duration** | Working days = remaining work ÷ (8 hours × number of engineers) |
| **Dependency** | A task starts only when the tasks before it finish |
| **Critical path** | The chain of tasks that sets the project end date |
| **Slack** | Days a task can slip without moving the project end date |
| **Baseline** | A saved copy of the original plan used to measure the change |

### 14.2 Working rules [SRC — S3, "to be confirmed"]
1. 8 working hours per day, **Monday to Saturday**.
2. **Work hours stay fixed**: fewer engineers make a task longer; more engineers make it shorter (effort-driven scheduling).
3. A moved engineer leaves on the move date and works **full time** on the new project from that day.
4. **No replacement:** if a task loses all its engineers, the rest of that project's team picks it up **after finishing their own tasks**.
5. New joiners work at **full speed** (a ramp-up factor, e.g., 80% for the first two weeks, can be added later).
6. Slip and gain are counted in **working days**.

### 14.3 Duration and finish arithmetic [DER — confirmed against S3 numbers in §15.8]
For a task with remaining work `W` (hours) and active assignments `i` on a day:
```
rate(day)        = Σ_i  hours_per_day(calendar) × allocation_i × factor_i(day)      # hours the crew produces on that working day
remaining(day+1) = remaining(day) − rate(day)
finish_day       = first working day where remaining ≤ 0
```
**Whole-day rule (confirmed by the source numbers):** a task occupies whole working days; a task whose last day is only partly used still ends on that day, and its successor starts on the **next whole working day**. Example: task 2.2 needs 14.67 days of a 3-person crew → occupies 15 days; successor 2.3 starts the following working day.
With a constant crew, `duration_days = ceil(W / (hours_per_day × Σ allocation × factor))`.

### 14.4 Dependencies, lags, constraints
- **Link types:** FS (default), SS, FF, SF, each with a lag (negative allowed = lead). *(MeridianPlan supports all four, as does MS Project.)*
- **Constraints:** *As soon as possible* (default), *Start no earlier than*, *Finish no later than*, *Must start on*, *Must finish on* (needed for customer-fixed dates such as FAT). A violated constraint is shown as a conflict, not silently moved.
- **Milestones:** zero-duration tasks.
- **Task types [PRO]:** *Fixed work* (default; effort-driven as in S3), *Fixed duration* (e.g., a FAT booked with the customer for 3 days regardless of crew), *Fixed units*.
- **Predecessors in S3 (reconstructed):** 1.2←1.1; 1.3←1.1; 1.4←1.2, 1.3; 1.5←1.4; 2.2←2.1; 2.3←2.2; 2.4←2.3; 3.2←3.1; 3.3←3.1; 3.4←3.2, 3.3. The document states 1.4's predecessors explicitly ("1.2, 1.3") and the critical path 1.1→1.3→1.4→1.5.

### 14.5 Critical-path method (CPM)
```
1. Order tasks topologically (cycle ⇒ report the cycle, refuse to compute).
2. Forward pass: ES = max over predecessors of (EF_pred + lag rules) and constraint dates; EF = ES + duration (resource-driven, §14.3).
3. Project finish = max EF.
4. Backward pass from project finish (or its constraint): LF, LS.
5. Total float  = LS − ES (working days). Free float = min(ES_succ) − EF − lag.
6. Critical = total float ≤ 0.   Critical path = chain(s) of critical tasks.
```
Slack check [VER, S3]: in Project 1, task 1.2 finishes 31 Oct and its successor 1.4 cannot start before 1.3 finishes on 7 Nov → total float of 1.2 = **6 working days** (2–7 Nov) ✓.

### 14.6 Resource-constrained behaviour [DER — an engine requirement exposed by S3]
S3's rule 4 ("the rest of that project's team picks it up after finishing their own tasks") and the Scenario-2 statement that **Twinkle can move with zero cost because Mahesh and Lalji have enough slack to finish her work** both require the engine to **sequence a person's tasks**, not merely add durations. Required behaviour:
1. If an engineer is assigned to two tasks that overlap, they cannot work both at 100% on the same day. The engine either (a) reports **over-assignment** (MS Project's default) or (b) applies a **priority rule**.
2. **Priority rule [PRO]:** critical tasks first, then least total float, then earliest ES, then task id. This is the rule the "after finishing their own tasks" wording implies, and it reproduces S3 (§15.8).
3. Over-assignment is always surfaced in the Engineer-Load view and the calculation trace; automatic multi-project **resource levelling** is out of scope for v1 (MS Project does it only on request; MeridianPlan omits it).

### 14.7 Progress and "work done" [SRC N17]
```
work_done(p, task, interval) = Σ over working days d in interval of
                               hours_per_day(d) × allocation_p × factor(p, task, d)
factor(p, task, d)           = skill_factor(p, task) × experience_factor(p, task, d)        (§9.6)
```
N17 states this as *calendar time since the engineer moved onto the task × factor (skill set and experience)*. Percent complete = Σ work_done ÷ total work. Actuals: *actual start*, *actual finish*, *remaining duration*, *physical % complete* (MeridianPlan keeps original and remaining duration and physical percent separately; V2 follows). Status date = "Today" unless set. **Retained logic** for out-of-sequence progress.

### 14.8 Baselines
A baseline is an immutable snapshot of a plan (task start/finish/work/assignments). Up to N baselines per plan (default 11, as in MS Project). Variance = current − baseline for start, finish, work, duration, cost. Scenario results are always expressed against a chosen baseline. Approving a scenario **promotes** its plan to a new baseline (§15.9).

### 14.9 Calendars
Default **Standard Mon–Sat, 8 h** (S3). Also: *Standard 5-day, 8 h* and *Continuous 24/7* (MeridianPlan ships "Standard · 5-day/8-hour", "Construction · 6-day/8-hour", "Continuous · 24/7"). Holidays and exceptions per calendar; a person can have their own calendar for capacity (§8.4). **Dates are civil, not DST-aware:** MeridianPlan stores integer civil minutes so schedules do not drift with daylight saving — V2 adopts that.

### 14.10 Determinism, traceability, performance
- Same inputs ⇒ same outputs (no wall-clock or randomness in the engine).
- Every computed date carries a **calculation trace** (which predecessor/constraint/resource rate set it) so a lead can answer "why does 1.5 finish on 28 Nov?".
- Incremental recompute of the dirty closure (as MeridianPlan does) so a scenario edit refreshes in well under a second for the whole portfolio (target: 50 projects × 500 tasks).

### 14.11 Where V2 deliberately differs from, or defers beyond, MS Project
| Area | V2 position |
|---|---|
| Resource levelling | Not in v1; priority sequencing (§14.6) only |
| Cross-project dependencies | Not in v1 (MeridianPlan lacks them). The master schedule shows projects side by side; shared-engineer coupling is handled through allocation, not links. Later: *inter-project milestone links* |
| Resource calendars in scheduling | v1 uses the task/project calendar; person calendars drive capacity and leave. Later: per-assignment calendars |
| Cost / earned value | Out of scope; hours-based variance only |
| Interchange | JSON (lossless), CSV, printable reports. **MS Project XML / P6 XER import** is a recommended later addition (neither the source nor MeridianPlan has it) |

---

## 15. What-if analysis (the most important module — S5, 7 Oct)

### 15.1 Purpose and the two canonical questions [SRC S3/S5]
Most resourcing decisions at SSM involve moving engineers between running projects. The module must answer, in MS Project terms:
1. **Move without replacement:** "If I move Mahesh from Project 1 to Project 2 and Project 1 gets no replacement, how much later does Project 1 finish, and how much earlier does Project 2 finish?"
2. **Major push:** "I want a major push on one project and want to see **the earliest date I can move various engineers** from their respective projects — and what each move costs the project they leave."

### 15.2 Scenario types [SRC N15, N16; PRO for the extensions]
| Type | Description | Source |
|---|---|---|
| **A. Move engineer** | Move one or more engineers from project X to Y from a date; full or partial (%); with or without replacement | S3 Scenario 1 |
| **B. Major push** | Choose a target project; the system finds eligible engineers, their earliest no-delay move dates, the cost of moving now, and builds a push plan | S3 Scenario 2 |
| **C. Move a skill** | Move *hours per week of a skill* (not a named person) from project X to Y; the system picks/ranks eligible engineers | S5 "moving engineers or rather skills" |
| **D. Remove capacity** | An engineer goes on leave / leaves / becomes part-time from a date | [PRO] |
| **E. Add capacity** | A new joiner/replacement arrives on a date (optionally with ramp-up) | S3 step 6, [PRO] |
| **F. Absorb new work** | An opportunity is won (with its demand lines and plan) — which projects slip? | [PRO] from pipeline §12.4 |
| **G. Delay or re-sequence** | Shift a project start/task constraint | [PRO] |
| **H. Combined** | Any set of A–G together; **no limit on the number of engineers** | S4 N16 |

### 15.3 Scenario inputs [SRC — S3 "Scenario_Moves" tab; extended]
| Field | Description |
|---|---|
| Scenario name, notes, baseline to compare to | |
| Engineer | The person (or skill + hours/week for type C) |
| From project / To project | |
| **Move date** | Effective date (working day) |
| **% moved** | 100% = full move; partial = splits the engineer's day |
| **Replacement** | None, or a named engineer/skill with an arrival date (and ramp-up) |
| Placement on receiving project | Default rule (§15.4 step 4) or an explicit task list |
| Factor profile | Compat (default) or Realistic (§9.6) |
| Options | Respect skill gates on/off (override requires a reason and optional mentor) |

### 15.4 Algorithm [DER — reconstructs S3's method; §15.8 verifies it]
```
Input: baseline plans of all projects touched; a list of moves M; settings.
1. Copy the baseline plans into scenario plans (never edit the baseline).
2. For each move m = (e, from P, to Q, date d, pct, replacement r):
   a. In every task of P with e assigned and not complete at d: end e's assignment at d (work done before d is kept).
   b. In Q: start e's assignment at d at pct × allocation, on the placement set (see 4).
   c. If r given: start r's assignment on P's tasks at r.arrival (with ramp-up, if profile = Realistic).
3. Apply the "lost-all-engineers" rule: a task in P whose crew becomes empty is picked up
   by the remaining P team *after they finish their own tasks* (priority rule, §14.6).
4. Placement rule on Q (default): the moved engineer is added to **every not-yet-complete task of Q, in sequence,
   from d**, i.e., they follow the project's work as it unfolds ("Mahesh joins 2.2, 2.3 and 2.4").
   (If a task is a fixed-duration task, e.g., FAT with a customer date, the crew change does not shorten it.)
5. Recompute all plans with the engine (§14): every task's start/finish, float, critical path, project finish.
6. Compare with the baseline: per task and per project, slip/gain in working days; critical path changes; per-engineer load.
7. Return the Impact (below) and persist the scenario with its inputs, settings and result.
```
**Why step 4 is stated explicitly:** the source never says *which* tasks the mover joins on the receiving project. The rule above is the one that reproduces **both** of S3's scenarios exactly (§15.8), so it is the default; the explicit task list is the override.

### 15.5 Skills inside the scenario engine [SRC N4, N7, N14]
- A move is **eligible** only if the engineer's rating meets each receiving task's minimum level (e.g., Project 2 needs SQL ≥ 3). Ineligible engineers appear in the push table as **"Not eligible (skill)"** (S3: Priyanka Makwana, SQL 2).
- Candidate order (when the user asks the system to *choose*): §9.7.
- Productivity factor and ramp-up: §9.6 (Compat reproduces S3; Realistic is optional).
- Type C (move a skill): the engine converts *hours/week of skill k* into candidate moves ranked by cost, so the user sees "to free 20 h/week of Asset Structure for Project B, the cheapest is X (slip 0 d), then Y (slip 3 d)".

### 15.6 Output (Impact) [SRC — S3 "Schedule", "Gantt", "Release_Analysis", "Engineer_Load" tabs]
1. **Schedule table:** for every task — scenario start/finish, **slip vs baseline** (working days), reason text (e.g., "Lalji alone — takes twice as long").
2. **Project table:** baseline finish, scenario finish, change (+/− working days), whether the **critical path** is hit, delayed/accelerated flags.
3. **Gantt:** baseline (grey) vs scenario (colour) bars, day by day.
4. **Engineer load:** hours per engineer per week with **red if over-allocated**.
5. **Release analysis** (type B): per engineer — current project, skill rating, *last day on current work*, **earliest move with no delay**, cost if moved on the chosen date to the current project and the gain to the target.
6. **Verdict text:** the decision question in words (e.g., "is gaining 7 more days on Project 2 worth delaying Project 1 by 9 days?").

### 15.6.1 Release analysis algorithm (type B) [DER]
For target project Q and each candidate engineer *e*:
```
slip(e, d)  = Δ finish of e's current project if e leaves on working day d      (monotonically non-increasing in d)
earliest_no_delay(e) = min d such that slip(e, d) = 0     (scan or bisect over working days)
gain(e, d)  = Δ finish of Q if e joins on d (placement rule §15.4 step 4)
```
Then build a **push plan** greedily: take engineers by earliest-no-delay date (cheapest first), apply each move to the scenario, **recompute** (gains are *not* additive), repeat until the target finish stops improving or the user stops. Also output the **curve "finish date of Q vs number of engineers moved"** — this answers "no limit on how many engineers" (N16) and shows diminishing returns. Engineers whose no-delay date is after the target's new finish are marked **"too late to help"** (S3: Mahesh and Lalji, free only on 19 Nov).

### 15.7 Comparing alternatives and deciding [SRC — S3 8-step flow]
1. **Record** the request (who/what/why).
2. **Keep** the current plan as the baseline.
3. **Define** the scenario (§15.3).
4. **Calculate** (engine recalculates all affected tasks).
5. **Read** the impact (§15.6): new finish per task/project, slip vs baseline, critical-path hit, gain.
6. **Compare** alternatives side by side — move now vs at the engineer's release date; full vs partial; with vs without replacement.
7. **Decide:** the owner approves or rejects (principle 15). The source's "both Project Leads agree" step is dropped by the owner's decision of 21 Oct.
8. **Apply:** update the plan, Resource_Allocation and the Change/Gap log; the result **becomes the new baseline**.
V2 enforces the sequence with an **approval workflow** (§17.4) — a scenario cannot be applied until required approvals exist, and "required" is computed (any project finish moved ⇒ leadership approval).

### 15.8 Verification of the source examples [VER — re-computed by hand for this document]
**Setup [SRC S3]:** three sample projects, start Monday **5 Oct 2026**; calendar Mon–Sat, 8 h/day. Work, crew and predecessors:

| Task | Crew | Work (h) | Crew rate (h/day) | Days | Baseline start–finish |
|---|---|---|---|---|---|
| 1.1 Design & detail | Mahesh | 96 | 8 | 12 | 5 Oct – 17 Oct |
| 1.2 Historian configuration | Mahesh, Lalji | 192 | 16 | 12 | 19 Oct – 31 Oct |
| 1.3 Dashboard development | Twinkle | 144 | 8 | 18 | 19 Oct – 7 Nov |
| 1.4 FAT | Mahesh, Lalji | 48 | 16 | 3 | 9 Nov – 11 Nov |
| 1.5 Site installation & commissioning | Mahesh, Lalji | 96 | 16 | 6 | 12 Nov – 18 Nov |
| 2.1 Design & detail | Chirag | 80 | 8 | 10 | 5 Oct – 15 Oct |
| 2.2 MES configuration | Chirag, Deep | 480 | 16 | 30 | 16 Oct – 19 Nov |
| 2.3 MES–SAP integration | Deep | 160 | 8 | 20 | 20 Nov – 12 Dec |
| 2.4 FAT | Chirag, Deep | 48 | 16 | 3 | 14 Dec – 16 Dec |
| 3.1 As-Is study | Sagar | 64 | 8 | 8 | 5 Oct – 13 Oct |
| 3.2 Report development | Priyanka, Dharati | 320 | 16 | 20 | 14 Oct – 5 Nov |
| 3.3 Custom pages | Sagar | 160 | 8 | 20 | 14 Oct – 5 Nov |
| 3.4 FAT | Sagar, Priyanka | 32 | 16 | 2 | 6 Nov – 7 Nov |

**Baseline [VER]:** every row above matches the source dates when Sundays (11, 18, 25 Oct; 1, 8, 15, 22, 29 Nov; 6, 13 Dec) are skipped. Baseline ends: **P1 18 Nov, P2 16 Dec, P3 7 Nov** ✓. Critical path of P1 = 1.1→1.3→1.4→1.5 ✓; 1.2 float = 6 days ✓.

**Scenario 1 — Mahesh moves from P1 to P2 on Mon 26 Oct, no replacement:**
| Step | Computation | Result | Source says |
|---|---|---|---|
| 1.2 | Done 19–24 Oct by 2 people = 6 d × 16 h = 96 h; remaining 96 h by Lalji alone (8 h/d) = 12 d from 26 Oct | ends **7 Nov** | +6 days ✓ |
| 1.4 FAT | starts after 1.2 and 1.3 (both 7 Nov) → 9 Nov; 48 h by Lalji alone = 6 d | ends **14 Nov** | +3 days ✓ |
| 1.5 | starts 16 Nov; 96 h alone = 12 d | ends **28 Nov** | +9 days ✓ |
| **P1 end** | | **28 Nov (+9 working days)** | **✓** |
| 2.2 | by 24 Oct: 8 d × 16 = 128 h done; remaining 352 h with 3 people (24 h/d) = 14.67 → 15 d from 26 Oct | ends **11 Nov** | |
| 2.3 | starts 12 Nov; Deep + Mahesh = 16 h/d; 160 h = 10 d | ends **23 Nov** | |
| 2.4 | starts 24 Nov; 3 people = 24 h/d; 48 h = 2 d | ends **25 Nov** | |
| **P2 end** | 16 Dec → 25 Nov = 18 working days earlier | **−18 days** | **✓** |
Placement rule used: Mahesh joins 2.2, 2.3, 2.4 (§15.4 step 4). 

**Alternative — Mahesh moves on Thu 19 Nov (after finishing P1 work):** P1 unaffected (ends 18 Nov). P2: 2.2 still ends 19 Nov (only 16 h remain that day); 2.3 with Deep + Mahesh = 10 d from 20 Nov → 1 Dec; 2.4 with 3 people = 2 d → **3 Dec**; 16 Dec → 3 Dec = **−11 days** ✓. Decision framing ✓: *"gaining 7 more days on P2 (−18 vs −11) costs 9 days of delay on P1."*

**Scenario 2 — major push on P2 (SQL ≥ 3 required), move date 26 Oct:**
| Engineer (current project) | SQL | Last day on current work | Earliest no-delay move | If moved 26 Oct: delay to current project | Verified |
|---|---|---|---|---|---|
| Twinkle (P1) | 3 | 7 Nov | **26 Oct (now)** | **0** | ✓ Her task 1.3 (critical) has 96 h left; Mahesh + Lalji finish 1.2 on 31 Oct, then do 1.3's 96 h as a 2-person crew = 6 d (2–7 Nov), so 1.3 still ends 7 Nov. |
| Dharati (P3) | 5 | 5 Nov | 6 Nov | **+10** | ✓ 3.2 left with Priyanka alone: 160 h = 20 d from 26 Oct → 17 Nov; FAT then 18–19 Nov vs 7 Nov = 10 working days late. |
| Sagar (P3) | 5 | 7 Nov | 9 Nov | **+7** | ✓ (assuming the FAT crew is the original minus the mover) 3.3 remaining 80 h picked up by Priyanka + Dharati after 3.2 (5 d → 11 Nov); FAT by Priyanka alone = 4 d → 16 Nov = 7 working days late. |
| Mahesh (P1) / Lalji (P1) | 5 / 3 | 18 Nov | 19 Nov | **+9** | ✓ same as Scenario 1 |
| Priyanka (P3) | 2 | 7 Nov | — | — | Not eligible (SQL 2 < 3) ✓ |
Each single move cuts **18 days** from P2 because every engineer adds 8 h/day — verified for Twinkle (2.2 → 11 Nov, 2.3 → 23 Nov, 2.4 → 25 Nov) ✓.

**Suggested push plan [SRC] — Twinkle from 26 Oct, Dharati from 6 Nov, Sagar from 9 Nov → P2 ends 18 Nov instead of 16 Dec (−24 working days):**
2.2: 128 h done by 24 Oct; with Chirag, Deep, Twinkle (24 h/d) for 10 days to 5 Nov = 240 h → 368; Dharati joins 6 Nov (32 h/d): 6–7 Nov → 432; Sagar joins 9 Nov (40 h/d): needs 48 h → ends **10 Nov**. 2.3 (160 h) crewed by Deep, Twinkle, Dharati, Sagar (32 h/d) = 5 d from 11 Nov → **16 Nov**. 2.4 (48 h) with all five (40 h/d) = 2 d → **17–18 Nov** ✓. 16 Dec → 18 Nov = **24 working days** ✓. P1 and P3 unaffected (Twinkle costs 0; Dharati and Sagar leave after their P3 work ended 5 and 7 Nov) ✓. Mahesh and Lalji free on 19 Nov — **too late** ✓.

**Conclusion:** the default rules in §14 and §15.4 reproduce **every** number in S3. These tables are the engine's **golden acceptance test** (§28).

### 15.9 After the decision
- A rejected scenario stays archived (with the reason) for audit.
- An approved scenario: (1) creates `ResourceMovement` records (§17); (2) updates allocations and task assignments; (3) writes a Change/Gap-log entry (kind *Decision*, impact hours/days, approver, date); (4) **promotes** the scenario plan to the project's new baseline (previous baseline retained); (5) notifies the affected leads.

### 15.10 Edge cases the engine must handle [PRO]
Move before the engineer starts; move after all their work is done (cost 0); several moves of the same engineer; engineer on parallel tasks; partial % (e.g., 50%); replacement arriving late; override of a skill gate; holiday inside the window; a task in progress at the move date (split work done/remaining); fixed-duration tasks; constraints violated by the new dates; a move that leaves a project with **no one qualified** for a remaining task (reported as an unfillable task with the missing skill, not as a fake date); over-allocation produced by the move (reported in Engineer-Load); and moves that make the receiving project's critical path switch.

### 15.11 Scenario workspace (UI) [PRO]
Left: scenario list and move editor (engineer picker with skill chips, date, %, replacement). Centre: baseline-vs-scenario **Gantt** for the affected projects. Right: **impact summary** (finish per project, slip/gain badges, critical-path flag). Bottom: engineer-load strip and the release-analysis table. Top: *Compare* (up to 4 scenarios side by side). Every number has a **Why?** link to its calculation trace.

---

## 16. Master schedule and the combined Gantt

### 16.1 Separate plans per project [SRC N9]
Each project owns its **own WBS and schedule**. There is no global shared task list. Engineers connect projects; tasks do not.

### 16.2 The master schedule shows level-1 only [SRC N10]
The master (portfolio) schedule lists **project names with their level-1 tasks beneath them** and does **not** drill into level-2. Detail opens the project's own plan.

**What is "level 1" in the existing trackers? [DER — OPEN Q9]** The tracker sheets are flat: each row has a *WBS* number (1.0, 2.0, …), a *Module* and a *Task Name*. The natural hierarchy is **Module (level 1) → Task (level 2)**, e.g., NTPC Korba: *Calibration Module*, *Calibration Method*, … Proposal: the importer builds a summary row per distinct *Module* (level 1) with start = min(task start), finish = max(task end), progress = work-weighted; the original rows become level 2. Leads may also define level-1 tasks explicitly.

### 16.3 Roll-up rules [PRO]
```
L1.start    = min(child.start)           L1.finish = max(child.finish)
L1.progress = Σ(child.work × child.progress) / Σ(child.work)     (fallback: duration-weighted)
L1.status   = Delayed if any critical child is Delayed; else In Progress / Completed / Pending by children
Project bar = [min L1.start, max L1.finish], with baseline and forecast finish markers
```

### 16.4 Combined Gantt requirements [SRC — user, 14 Oct: "combined Gantt which shows me all projects together properly"]
1. **All active projects on one timeline**, grouped by project, each expandable to its level-1 tasks; collapsed by default to one summary bar per project.
2. **Baseline vs current** bars (grey underlay) and a **forecast finish** diamond; **Today** line; milestone markers (FAT, SAT, go-live, payment).
3. **Status colouring** consistent with the trackers (Completed / In Progress / Pending-Future / Delayed) plus **critical** highlighting.
4. **Swim-lane modes:** by project (default), **by engineer** (each engineer's L1 tasks across projects, showing overlaps — i.e., parallel work), by technical manager, by product line (DOW/MES/Historian/LIMS/CMS/BATCH), by customer.
5. **Engineer-load strip** under the Gantt (hours per week per engineer; red when over-allocated) aligned to the same time axis, so a clash is visible at a glance.
6. **Filters:** PM, technical lead, team, product, health, status, customer, date window, "critical only", "delayed only".
7. **Zoom levels:** day / week / month / quarter; fit-to-window; pan; keyboard navigation.
8. **Drill-down:** double-click a project → its full plan; double-click an engineer lane → their allocation and tasks.
9. **Scenario overlay:** toggle a scenario to show baseline vs scenario for any project.
10. **Export:** PNG/PDF (print-ready), Excel, JSON; shareable read-only link inside the network.
11. **Performance:** smooth with ~50 projects × ~10 L1 tasks and a few thousand L2 tasks on drill-down.

### 16.5 Rendering approach [SRC/DER — MeridianPlan]
MeridianPlan renders with **WebGPU** and falls back to Canvas 2D, computes the schedule in a **Web Worker**, and stores plans in IndexedDB. For EOS V2: reuse its renderer and interaction layer (§27) for the Gantt canvas; compute schedules **server-side** (single source of truth for UI, API, CLI and MCP) and send the results to the client; keep a worker-side copy only for instant local edits.

### 16.6 Tracker → plan mapping [DER]
| Tracker column | V2 field |
|---|---|
| Sr | sort order |
| WBS (1.0, 2.0…) | WBS code (flat; hierarchy derived from Module) |
| Module | level-1 summary |
| Task Name | task name |
| Start Date / End Date | start / finish (as *planned*; baseline created on first import) |
| Duration (Days) | original duration |
| Progress % | percent complete |
| Status | status (Completed / In Progress / Pending-Future / Delayed) |
| Priority | priority |
| Owner / Engineer (initials) | assignment via alias (§6.4); "ALL"/"User" are special owners |
| Remaining Days | derived |
| Day-by-day cells | ignored (recomputed) |
**Data-quality notes [DER]:** the LMEL MES tracker has tasks with no dates (start/end 0) and a single engineer ("MU") on every row — those become *unscheduled* tasks (shown in a "needs dates" list, not in the Gantt); some project headers are blank (*Planned Completion* in LMEL). Work (hours) does **not** exist in the trackers — only duration; the importer therefore sets `work = duration × 8 h × crew size` as a *derived* value and marks it "estimated", so the what-if engine can run but its results are flagged lower-confidence until leads enter real work estimates.

### 16.7 Authoring plans: in EOS, or from the ERP, then extend [SRC — owner, 14 Oct]
A project plan (WBS and tasks) can come into existence in three ways, and all three end in the same editable plan:

1. **Create in EOS.** Build the WBS from scratch: summary tasks and tasks (indent/outdent, drag to reorder), milestones, durations or work, dependencies, calendar, constraints, crew assignment (with skill gates). Start from a **template** (a saved WBS, e.g., "X-Force MES project", "e-Logbook project", "Historian + Dashboard") configurable per product line. Templates, like everything, are editable data.
2. **Import from the ERP (or another system) and then add tasks.** The import creates the project plan from the ERP's WBS/task export; afterwards the user can add tasks, split or regroup them, set work estimates, link dependencies and assign crews. Re-importing **merges by external key** (task id/WBS code from the ERP): ERP fields it owns are updated, user-added tasks and user-edited fields are preserved and listed in the diff. **Which fields the ERP owns is a setting.** **[OPEN Q21]** The ERP files received so far (`XForce_Project_Review.xlsx`, 454 rows) carry **project-level** data only — no WBS or tasks. An ERP WBS/task export format, or the ERP's API, must be identified before this import can be built; until then the tracker workbooks (S8) are the task-level import source (§16.6).
3. **Import from tracker/Excel/MS Project** and extend: the S8 tracker mapping (§16.6); later MS Project XML.

**Editing capabilities (numbers are settings where applicable):** add/insert/delete/duplicate tasks and subtrees; reorder; change level; set original and remaining duration and **work (hours)**; fixed-work/fixed-duration/fixed-units; predecessors (FS/SS/FF/SF, lag); constraints; calendar per task; notes; custom fields; assignments with allocation % and optional *placeholder skill* (a task needing "SQL ≥ 3, 40 h" before a named engineer is chosen); percent complete and actuals; baselines; undo/redo; bulk edit; keyboard-first grid editing (as MeridianPlan's table). Every edit is audited and re-schedules incrementally.

**Task source of truth:** each task carries `origin` (`eos`, `erp`, `tracker`, `msproject`), `external_key`, and a per-field *locked-by-user* flag, so merges never overwrite deliberate edits.

---

## 17. Tracking resource movements [SRC N8]

### 17.1 The movement ledger
Every change of where an engineer's hours go is an **append-only `ResourceMovement` record** (§7.5):
`person, from_project, to_project, effective_date, percent_moved, replacement_person?, reason, requested_by, approved_by, approved_on, scenario_id?, status, impact snapshot`.
Allocations are *derived* from the ledger plus the initial state — so the engineer's full deployment history can be replayed to any date.

### 17.2 Types of movement
Full move, partial move (%), temporary loan with return date, replacement/backfill, joiner/leaver (start/end of availability), role change on project (e.g., Engineer → Lead), return to bench, and **mentor pairing** (a movement of the mentor's hours, §9.8).

### 17.3 Status lifecycle
`Requested → (Scenario analysed) → Approved | Rejected → Executed (on effective date) → Completed | Reverted`.
"Executed" is automatic on the effective date (the allocation flips), or manual if the move slips.

### 17.4 Approval and governance [SRC — S3 steps 7–8; S2 Change_Gap_Log]
- **The owner is the only approver** of every movement, scenario and decision [SRC — owner, 21 Oct]. Project leads may be consulted outside EOS but their agreement is neither required nor recorded as an approval. (S3 step 7 "both Project Leads agree" is dropped.)
- Each decision writes a **Change/Gap log** entry: *Log ID, Type, Project, Demand, Raised By, Date Raised, Description, Gap Type, Impact (Hrs), Impact (Days), Option/Decision, Approver, Decision Date, Status*. Types seen: *Capacity Gap*, *Change Request*, *Decision* (S2 sheet title: "WF-06, WF-09, WF-13"). Gap types seen: *Proficiency mismatch*.
- The approval record names who approved (always the owner). Only an Owner-role token can approve, reject, apply or execute (§23).

### 17.5 What the system shows
- **Per engineer:** timeline of projects, hours by month, movement history, current and upcoming allocations, free capacity.
- **Per project:** staffing history (who, when, % and hours), joiners/leavers, "churn rate", current demand fill.
- **Portfolio:** upcoming moves calendar; moves that changed project finish dates (with the slip); frequency of moves per engineer (excessive context switching); who has been on the bench and for how long.

### 17.6 Alerts
Pending approvals; moves effective within 7 days; engineer on > 2 concurrent projects; allocation > available hours; a project with a demand line unfilled within 14 days of its start; an engineer idle > N days.

---

## 18. Infrastructure: servers and VMs per project

### 18.1 What the source contains [SRC/DER — S10]
Workbook `X-Force ALL Server-VM Details.xlsx`, sheets:
| Sheet | Content |
|---|---|
| All DOW Publish Details | ~43 rows: server type (physical/VM), IP, purpose, internal/external XStudio publish and API addresses, application login name |
| Old / New / "(2)" Physical Server Details | Hosts with HDD free/total per drive (C, D, E, F), **RAM, CPU cores, logical processors**, and the VMs on each host with IP and purpose |
| ALL Demos | 9 demo environments (eLogbook, MES, …): customer, IP, system, URLs, SQL server, DB name |
| Update | Which engineers use which AD server (4 rows) |
| Sheet1 | **31 VMs**: host IP, VM IP, VM name, **State (Running / Off)**, project/purpose, assigned to, need backup, isdelete VM, VM removed |
**Credential columns in all sheets are excluded from EOS V2 (§2.4).**

### 18.2 Inventory snapshot [DER from Sheet1 and the physical-server sheets]
| Physical host | VMs listed | Notes |
|---|---|---|
| 10.2.6.46 | 7 | Base server; VMs incl. Sohar MES publish and database, Petronet LNG e-Logbook, MRPL POC; free HDD rose from 159.86 to 343.93 GB (of 953.24) between the old and new sheets (clean-up) |
| 10.2.6.55 | 7 | Base server; incl. Huber X-Batch; the new sheet shows only 63.5 of 476.31 GB free |
| 10.2.6.42 | 4 | Base server; VMs for Banco MES, LMEL MES, Pidilite MES; **C: has 0.56 of 93.87 GB free**; also the Windows runner host referenced for CI |
| 10.2.8.86 | 6 | |
| 10.2.8.88 | 7 | |
| 10.2.8.73 / 10.2.8.74 | — | In-house publish / demo servers (IOCL, GAIL; AD users listed) |
| 10.2.18.42 / 10.2.18.43 | — | Sohar Steel application and SQL database servers |
**VM state [DER]:** **7 Running, 24 Off** of 31 listed. Many *Off* VMs have purpose "Unknown", an assignee listed as a former project owner, "Need Backup = No" and "isdelete VM = Yes" — a **reclaim list**. (Specific IPs of VMs are in the source; V2 stores them as data, not in this document.)

### 18.3 Project ↔ VM mapping [DER examples; link table `ProjectHost`]
| Project | Role | Host / VM (by name) |
|---|---|---|
| Sohar Steel MES (PRO2503040) | Publish; Database | `PRO2503040_SSL_MES_Publish`, `PRO2503040_SSL_MES_Database` on host 10.2.6.46; application and SQL servers 10.2.18.x on site |
| Petronet LNG e-Logbook (V_PRO2602059_2) | Application | `V_PRO2602059_2_Petronet_LNG_Elogbook` on 10.2.6.46 |
| Huber X-Batch (PRO2506007) | Application | `PRO2506007_HUBER_XBATCH` on 10.2.6.55 |
| LMEL MES (PRO2602018) | Application | `PRO2602018_X_Force_MES_LMEL` (10.2.6.55, "Not Access") and `VM_LMEL_MES` (10.2.6.42) |
| Banco MES (PRO2502024) | Application | `VM_BANCO_MES` (10.2.6.42) |
| Pidilite MES | Application | `XForce_Pidilite_MES` (10.2.6.42) |
| EMAMI in-house publish | Publish | VM on 10.2.8.74 |
| IOCL on-site publish | Publish | VM on 10.2.6.54 |

### 18.4 Requirements [SRC — user: "VMs for the projects on which engineers are working"; PRO for detail]
1. **Inventory:** hosts and VMs with IP, name, role, state, RAM, CPU, disks (free/total), OS if known, purpose (publish / database / demo / POC / dev / test / production).
2. **Project link:** each VM belongs to one or more projects; a project page lists its environments and their state; an engineer page lists the VMs of the projects they work on.
3. **Owner and users:** an accountable engineer per VM; the AD/user list per server is stored as *people* links (no credentials).
4. **Lifecycle:** requested → provisioned → in use → idle → marked for deletion → removed, tied to the **project lifecycle** (closing a project creates a decommission checklist for its VMs; "VM running but project closed" is an alert).
5. **Capacity view:** per host, free disk %, VM count, running vs off, memory/CPU totals; **hosts below 10% free disk** (e.g., the 10.2.6.42 C: drive) are red.
6. **Backup tracking:** *need-backup* flag, last backup date, and policy; VMs flagged *needs backup* with no recent backup alert.
7. **Reclaim list:** Off + unknown purpose + marked deletion + no backup required → candidates; export for the infrastructure owner.
8. **Environment catalogue** (publish/demo/POC systems): product, customer, internal and external URLs — shared with leads, **without credentials**.
9. **Live state (later):** the downloads folder holds Hyper-V helper scripts (`find_vm_host.ps1`, `hypervvmmetrics 2.ps1`, `HyperV_VM_Stop_Diagnostics_V2.ps1`) and Grafana-style exports (*Running VM Status*, *Physical Server Status*), indicating existing monitoring. V2 should accept these as feeds so *State* is refreshed automatically rather than typed. **[OPEN Q12]**
10. **Incident link:** an RCA (e.g., the Banco MES VM failure RCA of 19 Aug 2026) attaches to the VM and project as an incident record.

### 18.5 Reports
VM-per-project register; host capacity; reclaim list; VMs without owner; VMs without backup; project environments for a given engineer; environment changes over time.

---

## 19. The project-management flow (engineering-management view)

This is the end-to-end lifecycle that EOS V2 supports. Each stage lists **purpose, inputs, activities, outputs (records created), owner, exit gate** and the EOS V2 features involved. Stages follow the evidence in the sources (S1, S3, S8, S9) and the owner's brief; where the sources are silent the stage is **[PRO]**.

### 19.0 Overview
```
Opportunity ─► Order & handover ─► Planning ─► Staffing ─► Execution & monitoring ─► Acceptance ─► Closure ─► Support / AMC
   (§19.1)         (§19.2)          (§19.3)     (§19.4)           (§19.5)              (§19.6)      (§19.7)      (§19.8)
        ▲                                              │  what-if (§15) / movements (§17) / training (§10) ─────────────┘
        └──────────── lessons learned, skills evidence, capacity released ◄───────────────────────────────────────────┘
```

### 19.1 Opportunity (pre-sales)
- **Purpose:** forecast engineering demand before an order exists. [SRC S2 Upcoming_Projects]
- **Inputs:** sales lead/proposal, scope outline.
- **Activities:** qualify; estimate **hours by skill** (and by month); set *stage* (Qualified / Proposal Sent / Negotiation…), *win probability*, *likely booking month*, *duration*; list *required skills*; identify an engineering lead.
- **Outputs:** `Opportunity` with `ProjectMonthHours` and required skills; weighted hours (= hours × probability) feeding §12.5.
- **Owner:** opportunity lead (e.g., the column *Project Lead*); technical manager reviews feasibility.
- **Exit gate:** order received → convert to project (won) or close (lost).
- **EOS features:** pipeline board, win-probability editor, forward demand heat map, "if we win it" what-if (type F).

### 19.2 Order and handover
- **Purpose:** turn an order into a managed project. [SRC S9 columns: Project No, Order No, Opportunity No, PO dates, Order Amount, Project Manager, Technical Manager, Delivery Leader, Area, Vertical]
- **Activities:** create the project (number, name, account, product line, type); assign **PM and Technical Manager**; record PO start/end and contractual completion; hold the **kick-off** (the ERP has an *IKOM* flag — likely internal kick-off; **[OPEN]**); copy the opportunity's demand lines and weighted hours; note customer site, location and subscription terms (e.g., LMEL's 1-year subscription Feb 2026–Feb 2027).
- **Outputs:** `Project` (status *Entered*), initial `Demand` lines, an *Environment request* (VM, §18).
- **Exit gate:** PM + TM agree scope, dates and an initial team.

### 19.3 Planning
- **Purpose:** a credible plan and a baseline. [SRC N9: separate WBS/schedule per project]
- **Activities:** build the WBS (modules → tasks); estimate **work in hours**; set dependencies/constraints (FAT dates); choose a calendar; propose crew per task (skills gates, §9.6); create the **baseline v0**; list risks, assumptions and dependencies on the customer; request environments (publish/demo/DB VMs); check training needs vs team skills (§10.7).
- **Outputs:** `Plan` (live + baseline 0), `Task`/`TaskLink`/`Assignment`, `RiskIssue` entries, `ProjectHost` requests, training-plan items.
- **Exit gate:** baseline approved by PM and TM.
- **EOS features:** project plan editor (MeridianPlan UI), critical-path view, plan-vs-capacity check ("this plan needs 3 Asset-Structure engineers in November; you have 2 free").

### 19.4 Staffing (demand → allocation)
- **Purpose:** fill each demand line. [SRC S2 WF-04/WF-05]
- **Activities:** for each demand: see eligible engineers (§12.1), ranked candidates (§9.7), free hours; **propose** (what-if) or **confirm** allocations; escalate gaps to the **Change/Gap log** with options (pair/mentor, re-time, borrow from another project via a scenario, train, hire).
- **Outputs:** `Allocation` (Proposed/Confirmed), `ChangeGapLogEntry`, `ResourceMovement` if moving someone.
- **Exit gate:** all critical demand lines *Filled* or an approved mitigation exists.

### 19.5 Execution and monitoring
- **Purpose:** deliver and keep the picture true. [SRC S8 weekly-updated trackers; "Last Updated 17 Sep 2026"]
- **Weekly status update (per project) [PRO standardising S8]:** update each task's *progress %*, *status*, *actual start/finish*, *remaining duration*, *owner*; mark delays with a reason; update *health* (computed, override allowed), *risks/issues*, *customer observations* (e.g., "14-09-2026: user observation pending" in the Korba notes), *environment state*.
- **Continuous activities:** timesheet/actual-hours import; resource moves and what-if (§15, §17); change requests (scope/date) with impact analysis via scenarios; **doubts and training** in parallel (§10); AMC/support interruptions (§13.6); VM and environment management (§18).
- **Cadence [PRO]:**
  | Meeting / review | Frequency | Inputs from EOS | Decisions |
  |---|---|---|---|
  | Project stand-up | Weekly | Project plan view, delayed tasks | Unblock tasks |
  | **Resource review** | Weekly | Capacity heat map, demand fill, over-allocations, pending moves | Approve/reject moves, assign gaps |
  | **Portfolio review** | Monthly | Combined Gantt, health table, forecast finishes, pipeline | Priorities, escalations |
  | **Skills & training review** | Monthly/Quarterly | Skill summary, bus-factor, training plans, doubts backlog | Training plan, pairings, hiring signals |
  | Infrastructure review | Monthly | Host capacity, reclaim list | Decommission, expand storage |
- **Outputs:** updated plan/health, change log entries, movement ledger, skill/training transactions.
- **Exit gate:** all scope tasks complete; FAT/UAT scheduled.

### 19.6 Acceptance and deployment
- **Purpose:** prove and hand over. [SRC S3 FAT tasks; S8 status "Deployed"; Korba notes "user observation pending"]
- **Activities:** FAT, UAT (with customer), SAT/commissioning, deployment, *observation period*. Fixed-date milestones with *fixed-duration* tasks (§14.4).
- **Outputs:** milestones (FAT/SAT/Go-live) with actual dates; status *Deployed*; punch list.
- **Exit gate:** customer acceptance.

### 19.7 Closure
- **Purpose:** release resources and capture learning. [PRO]
- **Checklist (auto-generated):** release remaining allocations (capacity appears in supply); **decommission or retain VMs** (§18); archive plan and final baseline; record **actual vs planned** hours and finish; capture **skill evidence** (`task_evidence` transactions) and training outcomes; collect **lessons learned**; request performance feedback inputs (links to V1 reviews); financial hand-off to PM (invoice status is read from the ERP master).
- **Outputs:** project status *Completed*, closure report (§20), updated skill ratings.

### 19.8 Support and AMC
- **Purpose:** keep customers running. [SRC S8 AMC sheet]
- **Flow:** ticket raised (customer + product + issue) → **triage** (required skill/proficiency, urgency) → **assign** (skill-matched, §13.6) → resolve → **root cause** recorded → follow-up/reference.
- **Metrics:** time to assign, time to resolve, SLA breaches, recurring root causes per product (e.g., database connectivity between two systems; slow report-generation queries), support hours per engineer (reduces capacity, §8.6).

### 19.9 RACI [PRO — roles as they appear in the sources; EOS V2 implements no access roles]
| Activity | Owner (leadership) | Technical Manager | Project Manager | Team lead | Engineer | Infra owner |
|---|---|---|---|---|---|---|
| Opportunity estimate | A | R | C | C | I | I |
| Project setup | I | A | R | C | I | I |
| Plan & baseline | I | A | R | R | C | I |
| Staffing & gaps | A | R | C | R | I | I |
| What-if and moves | A (approve) | R | C | R | I | I |
| Weekly status | I | A | R | R | R | I |
| Training & skills | I | A | I | R | R | I |
| Environments / VMs | I | C | C | C | I | A/R |
| Acceptance | I | A | R | R | R | C |
| Closure | I | A | R | R | C | R |
R = responsible, A = accountable, C = consulted, I = informed.

### 19.10 Records created per stage (traceability)
| Stage | Main records |
|---|---|
| Opportunity | Opportunity, ProjectMonthHours, Demand (forecast) |
| Order | Project, Milestone (contract), Environment request |
| Planning | Plan, Task, TaskLink, Assignment, baseline, RiskIssue |
| Staffing | Demand, Allocation, ChangeGapLogEntry, ResourceMovement |
| Execution | Task progress, Plan versions, Doubt, TrainingTransaction, SkillTransaction, AuditEvent |
| Acceptance | Milestone actuals, status change |
| Closure | Final baseline, closure report, released allocations, host decommission |
| Support | AmcTicket, support allocations |

---

## 20. Reports catalogue

All reports are available as **on-screen views**, **PDF/Excel/CSV exports**, **CLI** (`eos report <id> [--month …] [--format xlsx|pdf|csv|json]`) and **MCP** tools. Each is parameterised (period, team, project, product, engineer). "Cadence" is the recommended review rhythm.

### 20.1 Portfolio and projects
| ID | Report | Audience | Contents | Cadence |
|---|---|---|---|---|
| R-P1 | **Portfolio status** | Owner | One row per active project: product, PM/TM, status, progress %, computed health, delayed tasks, planned vs forecast finish, slip (working days), next milestone | Weekly |
| R-P2 | **Project one-pager** | Owner, PM | KPIs, mini-Gantt (L1), top risks, staffing (who, % and hours), demand fill, environments, recent changes | On demand |
| R-P3 | **Delay and slip report** | Owner, TM | Delayed tasks by project/module/owner with days late and reason; critical-path items | Weekly |
| R-P4 | **Planned vs actual** | PM | Baseline vs current dates and hours per task/project (variance) | Monthly |
| R-P5 | **Milestone tracker** | PM | FAT/SAT/Go-live/payment milestones, planned vs actual, upcoming 30/60/90 days | Weekly |
| R-P6 | **Pipeline report** | Owner | Opportunities by stage, hours, weighted hours, skills required, likely booking month | Monthly |
| R-P7 | **Commercial snapshot** | Owner | Order value, invoiced, pending invoice/payment (from ERP master) next to delivery health | Monthly |
| R-P8 | **Project closure report** | Owner | Actual vs planned effort/finish, resources used, lessons learned, VMs released, skill evidence | At closure |
| R-P9 | **Stale tracker report** | TM | Projects whose "last updated" is older than 7 days | Weekly |

### 20.2 Capacity, allocation and what-if
| ID | Report | Contents |
|---|---|---|
| R-C1 | **Capacity vs commitment** | Available, planned, allocated, free (planned and hard) per engineer/team/month with bands (§8.2) |
| R-C2 | **Engineer load (weekly)** | Hours per engineer per week by project; red when over-allocated (S3 tab "Engineer_Load") |
| R-C3 | **Who is working where** | Matrix engineer × project (current), with % and hours; bench list |
| R-C4 | **Demand fill / gap register** | Every demand line with status, eligible count, best-rated engineer, unfilled hours; gaps with options and decisions |
| R-C5 | **Forward supply–demand heat map** | Skills × months gap hours (§12.5) with pipeline weighting |
| R-C6 | **Scenario summary** | A scenario's inputs, impact table, Gantt comparison, verdict — printable for approval |
| R-C7 | **Scenario comparison** | Up to 4 scenarios side by side (finish dates, slips, load) |
| R-C8 | **Release analysis (major push)** | S3 Scenario-2 table + push-plan curve |
| R-C9 | **Movement history** | Ledger by engineer, project, period; moves that changed finish dates; churn |
| R-C10 | **Change/Gap log** | All entries with status, impact, decision, approver |
| R-C11 | **Utilisation trend** | Planned vs actual utilisation by month (needs timesheets) |

### 20.3 Skills and training
| ID | Report | Contents |
|---|---|---|
| R-S1 | **Skill matrix** | Engineers × skills heat map; avg rating; deployable count; stale/thin flags |
| R-S2 | **Skill summary** | Per skill and team: resources, deployable, competency %, required %, gap, bus-factor (S2 Skill_Summary) |
| R-S3 | **Skill transactions** | Ledger of assessments/changes with evidence |
| R-S4 | **Skill-gap vs demand** | Skills where projected demand exceeds deployable engineers |
| R-S5 | **Engineer skill profile** | One engineer: ratings, module Know %, history, training plan, projects worked |
| R-T1 | **Module coverage** | Modules × engineers (Know/Don't know/Need to discuss); modules known by too few people |
| R-T2 | **Team knowledge dashboard** | Know % per engineer, level (configurable bands), manager action (S6 Dashboard) |
| R-T3 | **Doubts backlog** | Open/assigned/hold/solved doubts, ageing, mentor load, repeated topics |
| R-T4 | **Training plan progress** | Plans by person with due dates and status |
| R-T5 | **Mentor workload** | Doubts and mentoring hours per mentor (flags single-mentor bottlenecks) |

### 20.4 Infrastructure
| ID | Report | Contents |
|---|---|---|
| R-I1 | **VM register by project** | Projects → environments → host/VM, state, owner (no credentials) |
| R-I2 | **Host capacity** | Disk free/total per drive, RAM, cores, VM counts; hosts below thresholds |
| R-I3 | **Reclaim list** | Off + unknown purpose + marked delete + no-backup |
| R-I4 | **Backup coverage** | VMs flagged need-backup vs last backup |
| R-I5 | **Environments for an engineer** | Which VMs an engineer's projects use |

### 20.5 Support and AMC
| ID | Report | Contents |
|---|---|---|
| R-A1 | **Ticket register** | By project/product/engineer/status |
| R-A2 | **SLA and responsiveness** | Raise→assign and raise→resolve times |
| R-A3 | **Root-cause analysis** | Recurring causes by product/customer |
| R-A4 | **Support load** | Hours per engineer on support, effect on capacity |

### 20.6 People and performance (from EOS V1)
Monthly performance, timesheet filing, peer reviews, attendance discipline, utilisation, per-engineer and team reports (the existing V1 report workbooks) remain available and are linked from the engineer and team pages. Performance reports are never fed into allocation decisions automatically.

### 20.7 Report definition standard [PRO]
Every report has: **ID, name, purpose, parameters, data sources (entities), formulas with units, filters, sort order, empty-state text, "thin data" markers, export layout, and an acceptance example**. Numbers show **n** (sample size) where averages are used, and never print a value for missing data (a dash is shown instead).

---

## 21. Dashboards

### 21.1 The Command Center (home) [SRC — user: "my main command center"; S1 MVP dashboard views]
S1's minimum viable dashboard lists six views — **Projects** (active, upcoming, delayed), **Capacity** (available vs committed hours), **Skills** (demand vs available capability), **Allocation** (who is working where), **Schedule** (how workload changes over time), **Opportunities** (what future demand may be coming). The Command Center delivers those six plus infrastructure and training:

| Zone | Widgets |
|---|---|
| **Top strip (KPIs)** | Active projects · At-risk projects · Delayed tasks · Engineers over-allocated · Free planned hours (next 4 weeks) · Open capacity gaps · Pending approvals · Open doubts · VMs running/off · Hosts low on disk |
| **Projects** | Portfolio health table (sortable), mini combined Gantt (L1), upcoming milestones (30 days) |
| **Capacity** | Capacity-vs-commitment bars by team and month; load heat map (engineer × week) |
| **Skills** | Demand vs supply by skill (forward 3 months); bus-factor list; training progress |
| **Allocation** | Who-is-where matrix; bench; upcoming moves |
| **Opportunities** | Pipeline by stage with weighted hours and the capacity they would consume |
| **Alerts feed** | Rule-driven (§22), each with *why* and *suggested action* |
| **What-if quick launch** | "Move an engineer…", "Push a project…", recent scenarios |
| **Infrastructure** | Environment status by project, reclaim candidates |

### 21.2 Other views
- **Team view:** one team's engineers, load, skills, training, doubts, projects.
- **Project view:** the project one-pager (R-P2) live.
- **Engineer view:** capacity, projects, skills, training, performance (V1), movements.
- **Infrastructure view:** hosts and VMs.
- **Skills & training view:** matrix and coverage.

### 21.3 Design requirements [DER — EOS V1 critique, 14 Oct]
A prior critique of the V1 Peer Insights page scored it 20/40 and flagged: missing values printed as numbers ("Tech 0.0"), undefined labels ("Reliable"), one word ("Coverage") meaning three different things, thin data styled as findings, dead space and 11 px text. V2 dashboards therefore must: **show a dash for missing data**, **define every metric on hover**, **use one meaning per term**, **mark thin data (n < 3) visibly**, avoid red/green-only encodings (colour-blind safe), and keep a minimum 12 px text size with real contrast.

---

## 22. Alerts and rules

A rule has: *condition, severity (Critical/Serious/Warning/Info), target (engineer/project/team), message template, suggested action, snooze/ack, notification channel*. Initial set:

| ID | Rule | Severity |
|---|---|---|
| A01 | Engineer allocated > 100% of available hours in any month/week | Serious |
| A02 | Engineer allocated > planned (target utilisation) but ≤ available | Warning |
| A03 | Demand line starts within 14 days and is not Filled | Serious |
| A04 | Demand has **no eligible engineer** (skill gap) | Critical |
| A05 | Skill has ≤ 3 deployable engineers and is in forward demand | Warning |
| A06 | Project health computed At Risk (§11.6) or forecast finish later than planned by > 5 working days | Serious |
| A07 | Task Delayed and on the critical path | Serious |
| A08 | Project tracker not updated for > 7 days | Warning |
| A09 | Plan has tasks without dates / without assignments / with unresolved person aliases | Warning |
| A10 | Scenario approved but movement effective date reached and not executed | Warning |
| A11 | Movement pending approval for > 3 days | Warning |
| A12 | Engineer on > 2 concurrent projects | Warning |
| A13 | Engineer idle (no allocation) for > N days | Info |
| A14 | Doubt unassigned > 2 days or open > 14 days | Warning |
| A15 | Engineer's skill rating stale (> 12 months) or thin (< 5 mapped modules) | Info |
| A16 | Rating disagreement between lead assessment and module evidence > 1.5 levels | Info |
| A17 | Host free disk < 10% | Serious |
| A18 | VM running for a Closed/Completed project | Warning |
| A19 | VM Off > 30 days, no owner, marked delete | Info (reclaim) |
| A20 | VM flagged need-backup without a recent backup | Warning |
| A21 | Data-quality: ambiguous/unresolved aliases, duplicate project numbers, planned dates in the past with open tasks | Info |
Alert thresholds are settings. Every alert opens the record and shows the rule and the numbers that triggered it.

---

## 23. Users, teams, access and audit

### 23.1 Users [SRC — user, 14 Oct; superseded 21 Oct]
- **Now:** one user — the owner. **Later (owner, 21 Oct):** engineers get access so they can fill in and use their own data. Team leaders have no decision rights. Access: office network or Tailscale.
- **Roles:** exactly two. **Owner** — everything. **Engineer** — read and write only records about themselves (self-assessment, peer reviews they give, their training knowledge states and doubts, leave requests, their own profile fields), and read-only on what the owner chooses to share with them. An engineer cannot approve, plan, allocate, rate, change settings or see another engineer's performance data.
- **Gate:** no Engineer token is issued until roles are enforced server-side on every endpoint (default deny) and covered by tests. Until then every token is an Owner token and only the owner holds one.

### 23.2 What is still required
- **Identity of the actor** on every change (who approved, who edited) — a named login per person, even without roles. [PRO]
- **Audit log** of all writes (who, when, what, before/after) — cheap now, expensive later. [PRO]
- **Authentication:** a single shared secret would not distinguish people; so one **personal access token per person** (stored hashed), used for CLI/API/MCP; the web UI signs in once per device with the same token, issued as a secure cookie. [PRO]
- **Network exposure:** bind to the LAN and Tailscale interfaces only; never publish to the public internet. [SRC — user: office network or Tailscale]
- **Seam for the future:** a `ClaimsPrincipal` pipeline so roles can be added later without changing endpoints. [PRO]

### 23.3 Sensitive data note
EOS holds performance reviews and personal data for about 24–27 engineers. "Show all info to everybody" is acceptable **because** access is limited to four named team leaders; it stops being acceptable the moment an engineer account exists. The audit log and per-person tokens make that boundary enforceable.

---

## 24. Data migration and imports

### 24.1 Principles
- **Idempotent:** re-importing the same file updates matched rows, never duplicates (natural keys below). [Continues EOS V1 behaviour; V1 also offers an import *preview/diff*.]
- **Preview first:** every import shows *rows to add/update/unchanged/reject* before committing.
- **Provenance:** each imported row keeps *source file, sheet, row number, import id*; imports are listed in history (V1 `ImportHistory`).
- **Quarantine, don't guess:** unresolved people, unknown skills, bad dates go to a review list.
- **Sample-data guard:** rows marked SAMPLE (the S2 projects/demands/allocations) import into a **Sample** space that can be hidden/purged, so dummy values never pollute real reports.
- **No credentials:** username/password columns are never read (§24.9).

### 24.2 Import map
| # | Source | Target entities | Natural key | Notes |
|---|---|---|---|---|
| I1 | S2 `Engineers` | Person, Team, Availability | Emp ID | Role/Location/Availability flagged *unverified (sample)* |
| I2 | S2 `Skills` + scale | Skill, SkillLevel | Skill ID | Scale text for 2–4 is proposed wording |
| I3 | S2 `Skill_Matrix` | PersonSkill, SkillTransaction (kind assessment) | (person, skill) | `Assessed Date`/`Assessed By` are dummy → set to import date and "initial import" |
| I4 | S7 `Ratings` | PersonSkill | (person, skill) | Same ratings as I3 → verify equality, report differences |
| I5 | S2 `Running_Projects` / `Upcoming_Projects` | Project/Opportunity, ProjectMonthHours | Project ID | **Sample**; Upcoming has real structure (stage, probability, skills) |
| I6 | S2 `Project_Demands` | Demand | Demand ID | **Sample** |
| I7 | S2 `Resource_Allocation` | Allocation | (engineer, project, demand) | **Sample**; *Proposed* rows → what-if |
| I8 | S2 `Change_Gap_Log` | ChangeGapLogEntry | Log ID | One sample entry |
| I9 | S6 `Module` | Module, PersonModule | (product, module, sub-module) | 132 modules × 27 engineer columns; normalise spellings |
| I10 | S6 `Module Proficiency Level` | PersonModule.proficiency | same | Default "Beginner" → import as *not set*, not as a real level |
| I11 | S6 `Need to Discuss` | Doubt | Sr No | Dates as given |
| I12 | S9 `ProjectList` | Project (portfolio) | Project No | 454 rows; strip HTML in OIGM; link `W_`/`V_` to parent |
| I13 | S9 `Sheet2` | Project.pm/tm (of record) | Project No | PM/TM assignments |
| I14 | S8 `LPP` | Project (live), tracker status | P. No | 11 rows |
| I15 | S8 per-project sheets | Plan, Task, Assignment (via aliases) | (project, Sr) | §16.6 mapping; work estimated from duration |
| I16 | S8 `AMC` | AmcTicket | (P. No, raised date, issue hash) | |
| I17 | S10 `Sheet1` + physical-server sheets + ALL Demos + Update | Host, HostDisk, Environment, ProjectHost, person↔host | IP (+name) | **Skip all credential columns** |
| I19 | **Every ERP export we have** (`eos import files <xlsx\|zip>`, Imports page, `POST /imports/files`), type auto-detected from the contents: project list (`Project No`, `Order Amount`, `Main Status`; also the `ProjectList` sheet of the review workbook), employee roster (`Employee No`, `Full Name`, `Band Level`), monthly utilisation summary (`Total Month Hours`, `Utilization`), detailed timesheets (`Project No`, `Total work Hours`), timesheet approval report (`Filled Date`, `Total Task Hours`, `Hrs`), attendance/leave/UAA export (`Punch Duration`, `UAA Status`), project task list (`Task No`, `Timesheet Filled Hours`, `Project Task Status`), engineer review workbooks (`Template Metadata`) | Project, Person, TimesheetEntry, MonthlyPerformance, Leave, PlanTask (origin erp) + assignment, PeerReview | project no / employee code / (person, date, entry) / (person, month) / ERP task no / (reviewer, subject, month) | **EOS reads the raw files, never only the V1 database.** A zip may hold any mix; files are applied in dependency order (roster, project list, timesheets, task list, summary, attendance, reviews). Detailed timesheets and ERP leave **replace**, per person, what is stored inside the file's date window, so an entry edited in the ERP is not counted twice and leave entered in EOS is never touched. Leave rows are built from approved leave days (half days = 0.5). The task list creates plan tasks (budget hours, hours booked, status, one engineer each) under a module group per milestone; tasks of a project the project list lacks get a stub project that the next project-list import completes; tasks without a project (common support, general) are only counted. A summary spanning more than one month is refused. A code embedding a known ERP number (`V_PRO2602059_2`) links to it. The operational score is V1's formula with every weight a setting. V1's up-down roster flag has no EOS field and is not imported; personal columns (address, birth date, contact) are never read. Other files in Downloads (customer tag lists, RDP logs, project dossiers) are not ERP data. |
| I18 | Later | Leave (`LV_LeaveSummaryforRP`), timesheets (`RPwiseTimesheetUtilazationReport`), roster (`List_Employee_Generalteam`), customer schedules (JSW Khopoli, RM Scope/Modules) | | See §24.8 |
The V1 *roster*, *reviews* and *performance* imports carry over unchanged (V1 `import-reviews`, source imports).

### 24.3 Import order
People/Teams → Skills → PersonSkill → Modules/PersonModule/Doubts → Projects (portfolio, then live) → Plans/Tasks (needs aliases) → Opportunities/Demands/Allocations → Infrastructure → AMC.

### 24.4 Counts to assert after import [DER — acceptance numbers]
| Entity | Expected |
|---|---|
| Persons from S2 | 24 (13 + 3 + 3 + 4 + 1) |
| Skills | 10 |
| Skill ratings | 23 engineers × 10 skills (rows E001–E022 plus Kartik Patil listed as E024; Srushti Gandhi has no ratings) |
| Modules | 132 (83 / 34 / 3 / 12) |
| Module states | Know 2,022 · don't Know 1,502 · Need to discuss 40 (27 engineers) |
| Logged doubts | 15 (8 Solved, 2 Assign, 1 Hold, 4 blank) |
| Portfolio projects | 454 (CLS 192, RLS 88, TECO 64, FUTURE ORDER 62, HLD 48) |
| Live LPP projects | 11 |
| VMs (Sheet1) | 31 (7 Running, 24 Off) |
| Pipeline | 4 opportunities, weighted hours 492.8 / 712.8 / 0 / 1,267.2 |

### 24.5 Data-quality register [DER — findings from reading the sources]
| # | Finding | Handling |
|---|---|---|
| DQ1 | S2 Engineers uses E023 = Kartik Patil, E024 = Srushti Gandhi; S2 Skill_Matrix lists **Kartik Patil as E024** and omits Srushti | Match by name; report the ID conflict |
| DQ2 | S6 Dashboard says **21** engineers; the Module sheet has **27** engineer columns | Use the 27; flag |
| DQ3 | Name spelling differs: Aayush/Ayush, Ziaul/Ziual, "Dahrmesh" (AD list) | Alias layer |
| DQ4 | S6 has people not in S2 (Safwan, Krinav, Priyen, Udaykumar) | Create as *unassigned persons* pending team assignment |
| DQ5 | S6 proficiency sheet is all "Beginner" (3,456) except 2 "Expert" | Treat as unset |
| DQ6 | Level-label vocabularies differ (Good/Medium/Weak/Critical vs Fast/Good/Medium/Slow) | Configurable bands |
| DQ7 | S6 dashboard "Total doubts" 71 vs 15 logged | Define doubt (Q6) |
| DQ8 | Skill ratings vs module Know % disagree for some people (e.g., Abhinath) | Flag rating-evidence disagreement |
| DQ9 | 160 h/month vs Mon–Sat 8 h scheduling calendar | §8.4 |
| DQ10 | Tracker engineers are initials; several ambiguous | Alias screen |
| DQ11 | LMEL tracker tasks without dates; planned completion blank | Unscheduled list |
| DQ12 | Tracker has no work-hours, only durations | Derived "estimated" work |
| DQ13 | Technical-manager and planned-date mismatches between S8 and S9 (Sohar, Pidilite) | Keep both; flag |
| DQ14 | Export artefacts: HTML in OIGM; `W_`/`V_` shadow projects | Strip; link parents |
| DQ15 | Dates in the tracker "Today (12-Oct)" and "Last updated 17 Sep" are manual | Use status date from import |
| DQ16 | S2 projects, demands, allocations and dates are **sample** | Sample space |
| DQ17 | S10 contains plaintext credentials | Never import (§24.9) |
| DQ18 | S10 many VMs "Unknown" purpose; "Not Access" entries | Mark *Unknown*/*NoAccess* states |

### 24.6 Validation rules (reject or warn)
Duplicate natural keys; rating outside 1–5; availability outside 0–1; end before start; negative hours; demand start/end outside project dates (warn); allocation to a project that does not exist; progress outside 0–100; unknown skill/module (create-or-reject choice); dates in 1970/0 (treated as missing).

### 24.7 Re-import and change detection
On re-import, the system computes a diff by natural key and shows changed fields; changed *ratings* are recorded as **skill transactions** (so progress over time is visible), changed *task dates* as **plan revisions**, changed *VM state* as **host state history**.

### 24.8 Later feeds worth adding [DER — observed in the downloads folder]
Leave (`LV_LeaveSummaryforRP`) → availability; timesheet utilisation (`RPwiseTimesheetUtilazationReport`) → actual hours; employee list (`List_Employee_Generalteam`) → roster; customer schedules (`JSW_Khopoli_*` resource and readiness schedules, `RM Scope & All Modules Schedule`, `L3 Deliverables RM Tracker`, `LMEL FAT`) → plans; Hyper-V/monitoring exports → host state; server-sizing calculators (`XStudio_MES_Server_Sizing_Calculator_*`) → environment requests.

### 24.9 The credential rule
The VM workbook contains passwords. The importer reads **only** the whitelist of columns in I17 (IP, VM name, state, purpose, project/owner text, drive sizes, RAM/CPU, URLs, flags). Any column whose header matches `user|username|password|pwd|login|credential|secret` is **not read, not logged and not stored**. A unit test ships with the importer that feeds a synthetic workbook containing such columns and asserts none reach the database or the logs. [PRO]

---

## 25. Platform and architecture

### 25.1 Decisions already taken [SRC — user, 14 Oct]
- Rebuild as **EOS V2**; EOS V1 (this repository's current code) stays untouched and keeps working.
- **Always-on server** on the **Ubuntu GitLab VM**; accessed over the office LAN and Tailscale.
- Exposes **everything**: **web UI, REST API, CLI and MCP**.
- Up to 4 users in future (team leaders), all seeing all data, no roles for now.
- A **Windows runner at 10.2.6.42** is available for Windows-specific builds and tests.
- The always-on Azure "EOS VM" of the earlier ops work is **not** the target.

### 25.2 Facts about the VM [SRC — reported by the VM session, read-only checks, 14 Oct]
| Item | Value |
|---|---|
| OS | Ubuntu 26.04.1 LTS, kernel 7.0.0-38 |
| Resources | 8 CPU, 12 GiB RAM (~6 GiB available), root disk 124 G with 72 G free (single partition) |
| Ports in use | 22, 3333 (MCP), 3389, 8060, 8080 (GitLab), 8090 (Pages) and local-only 8082, 8091, 8092, 8150–8155, 8181, 9229, 9236 → **8095 is free** for EOS |
| .NET | `/opt/dotnet`, SDK **10.0.204** (+ 6, 8), ASP.NET Core and NETCore **10.0.8** runtimes → framework-dependent publish works |
| Services | systemd; existing units run as user `snehil` (no dedicated service users); `snehil` has passwordless sudo |
| Runner | `ubuntusb-shell` (shell executor, concurrent = 1) at the GitLab URL; jobs run as `gitlab-runner`; **no sudoers rule for it** |
| Firewall | `ufw` **inactive** (no firewall) |
| Network names | Tailscale `100.110.236.122`, tailnet `ubuntusb.tail471da7.ts.net`; `tailscale serve` unconfigured; HTTPS certs not verified |
| Backups | no general VM backup; GitLab backups are manual tars (7-day retention, no timer) |

### 25.3 Architecture
```
                    ┌──────────────────────── Ubuntu VM (systemd unit "eos", user "eos") ───────────────────────┐
  Browser  ───────► │  EngineeringPerformance.Server  (ASP.NET Core, .NET 10)                                      │
  eos CLI  ───────► │   /            Blazor Server UI  (existing Razor UI library, extended)                      │
  Agents (MCP) ───► │   /api/v1/*    REST + OpenAPI                                                              │
  (later) WPF ────► │   /mcp         MCP endpoint (same application services)                                    │
                    │   /hubs/*      SignalR (live updates)        /health   /metrics                            │
                    │        │                                                                                    │
                    │        ▼  Application services  (Scheduling engine · Capacity · Skills · What-if · Reports) │
                    │        ▼  Infrastructure  (EF Core + SQLite WAL at /var/lib/eos · file store · importers)    │
                    └────────────────────────────────────────────────────────────────────────────────────────────┘
```
**One process owns the database.** CLI, MCP and UI are clients of the same services, so there is a single writer — which removes the "CLI while the desktop app is open" concurrency problem of V1.

### 25.4 Solution layout [PRO]
```
EOS_V2/
  src/
    EOS.Domain/            entities, value objects, enumerations (§7)
    EOS.Scheduling/        the scheduling & what-if engine (pure, no I/O) — C# port of MeridianPlan semantics (§27)
    EOS.Application/       use-cases, interfaces, DTOs, validation, alert rules, report definitions
    EOS.Infrastructure/    EF Core SQLite, importers (I1–I18), backup, file/report generators (ClosedXML), diagnostics
    EOS.Server/            ASP.NET Core host: REST, Blazor Server, MCP, SignalR, auth, audit
    EOS.UI/                Razor components + design system + Gantt (wraps the MeridianPlan renderer)
    EOS.Cli/               `eos` — thin client of the REST API (+ optional --local direct mode)
    EOS.Mcp/               MCP tool definitions over Application services
    EOS.DesktopHost/       (optional) WPF/WebView2 shell that points at the server
  tests/                   unit · golden scheduling · import · API · CLI parity · UI (Playwright)
  third_party/meridianplan/   MIT-licensed reference code + LICENSE + NOTICE
  docs/                    this specification and generated reference
  build/ deploy/           CI, systemd unit, backup timer, publish scripts
```

### 25.5 Technology choices
| Area | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 (matches V1, XS Builder, VM runtime) | Reuse of V1 domain/infrastructure; VM has the runtime |
| API | ASP.NET Core minimal APIs + OpenAPI; versioned `/api/v1` | CLI/MCP/UI parity; generated client |
| UI | Blazor Server using the existing Razor class library | Reuse; no separate front-end toolchain. (The question of a React/shadcn rewrite was discussed and **deferred**; see §26.3.) |
| Styling | Tailwind (already in V1; standalone CLI, no Node) with the V1 "instrument" design tokens | Continuity; the V1 design system document applies |
| Database | **SQLite (WAL)** on local disk | ≤ 4 users, small data; simplest to run and back up. EF Core migrations. PostgreSQL is a later option if multi-writer scale is needed |
| Gantt/scheduling UI | MeridianPlan renderer (JS, MIT) embedded; engine **ported to C#** | §27 |
| CLI | `System.CommandLine`, REST client | Typed options, `--json`, completions |
| MCP | Official C# MCP SDK | Agents call the same services |
| Live updates | SignalR | Dashboards refresh without polling |
| Logging | Serilog (file) + structured audit table | V1 practice |
| Tests | xUnit; Playwright (run on the **Windows runner 10.2.6.42** and Linux) | Existing runners |
| Reports | ClosedXML (Excel), a PDF renderer | V1 uses ClosedXML |

### 25.6 REST API surface (resources) [PRO]
All under `/api/v1`, JSON, standard paging/filtering (`?q=&team=&project=&from=&to=&page=`), `If-Match` row versions, problem+json errors.
```
people, teams, calendars, availability, leave, aliases
skills, skill-levels, skill-matrix, skill-transactions, modules, person-modules, doubts, training-plans, training-transactions, required-skill-levels
projects, opportunities, milestones, risks-issues, amc-tickets, deliverables
plans (live|baseline|scenario), tasks, task-links, assignments, baselines
demands, allocations, movements, change-gap-log, approvals
scenarios, scenarios/{id}/moves, scenarios/{id}/run, scenarios/compare, release-analysis
hosts, host-disks, environments, project-hosts
capacity (computed), supply-demand (computed), alerts, reports/{id}, dashboards/{id}
imports (preview/commit/history), exports, backups, diagnostics, settings, audit
```
Computed endpoints (`capacity`, `supply-demand`, `scenarios/{id}/run`, `release-analysis`, `reports`) are **GET/POST without side effects** except `run`, which stores a scenario result.

### 25.7 CLI (`eos`) command map [PRO]
Grammar: `eos <noun> <verb> [args] [--month yyyy-MM] [--json] [--server URL] [--token T] [--yes] [--dry-run]`. Exit codes: 0 success · 1 operation failed/partial · 2 usage · 3 not found · 4 conflict · 5 unauthorised.
| Group | Commands |
|---|---|
| **import** | `import preview|commit <kind> <file>` (kinds: roster, skills, modules, projects, tracker, vms, amc, reviews, …), `import history` |
| **person / team** | `person list|show|add|update|remove|alias`, `team list|add|rename|delete|assign|leader`, `availability set`, `leave add` |
| **skill / training** | `skill list|matrix|summary|rate|history`, `module list|add|map`, `training know|plan|session`, `doubt list|raise|assign|solve` |
| **project** | `project list|show|add|update|status|health`, `opportunity list|add|win|lose`, `milestone`, `risk`, `amc list|add|assign|resolve` |
| **plan** | `plan show|tasks|add-task|link|assign|progress|baseline|critical-path|trace`, `plan import|export` |
| **demand / allocate** | `demand list|add|fill`, `allocate list|propose|confirm|release`, `capacity show --team --month`, `supply-demand --skills --months` |
| **what-if** | `scenario create|move|skill|push|run|compare|approve|reject|apply`, `release-analysis --project` |
| **movement / log** | `movement list|request|approve|execute|revert`, `log list|add|decide` |
| **infra** | `host list|show|state|link|reclaim`, `env list`, `host capacity` |
| **report** | `report list`, `report run <id> --format xlsx|pdf|csv|json --out FILE` |
| **ops** | `backup export|list|restore`, `diag info|log|bundle`, `settings get|set`, `audit list`, `alerts list|ack` |
| **V1 features** | `perf month|history|weekly`, `review import`, `scoring show|set|preset`, `template generate`, existing `import-reviews` |
**Parity rule:** a test enumerates every application-service method and fails if it has no CLI mapping and no entry in an explicit exemption list. This is how "everything has a CLI flag" stays true over time.

### 25.8 MCP tools [PRO]
Read tools (default on): `list_projects`, `get_project`, `portfolio_status`, `get_plan`, `critical_path`, `combined_gantt_data`, `list_people`, `person_profile`, `skill_matrix`, `skill_gaps`, `capacity_overview`, `engineer_load`, `demand_gaps`, `supply_demand`, `list_scenarios`, `get_scenario`, `release_analysis`, `list_hosts`, `project_environments`, `list_doubts`, `list_alerts`, `search`.
What-if tools (side-effect-free, store results as a scenario): `simulate_move`, `simulate_push`, `simulate_skill_move`, `compare_scenarios`.
Write tools (explicit, confirmation token required): `create_scenario`, `approve_scenario`, `update_task_progress`, `record_doubt`, `assign_doubt`, `set_skill_rating`, `add_allocation`, `import_file`. Every write is audited with the calling token's name.
Example agent questions: *"Who can take 20 h/week of Asset Structure from November?"*, *"What happens to Sohar if Mahesh moves to LMEL on 2 Nov?"*, *"Which VMs belong to projects that are completed?"*.

### 25.9 Deployment on the VM [PRO — items marked ⚠ need the owner's approval]
1. Create a dedicated `eos` system user; data in `/var/lib/eos` (db, backups, imports, logs).
2. systemd unit `eos.service` (`Restart=always`, `WorkingDirectory=/opt/eos/current`, `ASPNETCORE_URLS=http://<tailscale-ip>:8095;http://<lan-ip>:8095`, `EnvironmentFile=/etc/eos/eos.env` for tokens).
3. Bind to the Tailscale and LAN addresses explicitly, **not** `0.0.0.0` — the VM has no firewall. ⚠ Optionally enable `ufw` with rules for 22/8080/8090/3333/8095 from the office and tailnet ranges.
4. **HTTPS:** plain HTTP over Tailscale is encrypted by the tunnel; for the office LAN path prefer `tailscale serve` (HTTPS on the tailnet name) or a reverse proxy — verify certificate availability first.
5. **CI/CD (self-hosted GitLab only, per policy):** `.gitlab-ci.yml` with stages *build → test → publish → deploy*; Linux jobs on `ubuntusb-shell` (concurrent = 1 → queue), Windows/Playwright jobs on the **10.2.6.42** runner. Deploy = copy the publish output to `/opt/eos/releases/<ver>`, flip a `current` symlink, `systemctl restart eos`, health-check `/health`, roll back on failure. ⚠ This needs a narrowly scoped `/etc/sudoers.d/` rule letting `gitlab-runner` run only `systemctl restart eos` (and the symlink flip). No such rule exists today.
6. **Backups:** a systemd timer runs nightly `sqlite3 … ".backup"` to `/var/lib/eos/backups/eos-YYYYMMDD.db` (retention 14 daily + 12 monthly), plus an **off-VM copy** (the VM is a single disk; no general backup exists). Restore is a documented, tested procedure (`eos backup restore`).
7. **Observability:** `/health` (liveness/readiness incl. DB), `/metrics`, rolling logs, a diagnostics bundle (`eos diag bundle`, as in V1).
8. **Migration from V1:** `eos backup export` on this PC → copy → `eos backup restore` on the VM → V2 migrations run → V1 data appears under People/Performance (§26).
9. **Release/versioning:** `main` = latest; annotated tags `v2.x.y`; GitLab repo `ssm/eos-v2` (proposed; V1 stays at `ssm/EOS`).
10. **Resource budget:** the VM has ~6 GiB free RAM and a concurrency-1 runner; the server must idle under ~300 MB and builds must not run during working hours if they starve GitLab. [PRO]

### 25.10 Non-functional requirements
| Quality | Target |
|---|---|
| Scenario run (portfolio of ~50 projects, ~5,000 tasks) | < 1 s p95; single move < 300 ms |
| Page load (dashboard) | < 2 s on LAN |
| Gantt (50 projects, L1 view; 5,000 tasks drilled) | 60 fps pan/zoom with WebGPU; ≥ 30 fps Canvas fallback |
| Availability | Restart-on-failure; nightly backup; RPO ≤ 24 h, RTO ≤ 1 h |
| Security | Tokens hashed; LAN/Tailscale only; audit log; no credentials stored; input validation; CSRF/XSS protections of ASP.NET Core |
| Accessibility | Keyboard-navigable; colour-blind-safe encodings; text ≥ 12 px |
| Determinism | Engine golden tests (§28) must pass bit-for-bit in CI |
| Maintainability | Code metrics gate (as in XS Builder), warnings as errors in the engine and domain |

---

## 26. Relationship to EOS V1

### 26.1 What carries over
Domain entities (Employee → Person), performance import and scoring, peer reviews and insights, timesheet filing, reports/templates, backup/restore, custom employee columns, the `eos import-reviews` command, the design system and critique findings.

### 26.2 What changes
The Blazor Hybrid WPF shell is no longer the only host. The server owns the data; the CLI becomes an API client; analytics logic that lived in the UI project (`Analytics.cs`: anomalies, quartiles, radar, compliance, peer networks) moves into `Application` so UI, API, CLI and MCP share it; report-data assembly moves out of Razor pages into services.

### 26.3 Front-end technology decision (recorded)
The owner questioned whether Blazor is good enough and asked about Tailwind/shadcn. Findings: V1 **already uses Tailwind**; the identified UI problems are information-design and UX issues (missing-data printing, undefined metrics, thin data styled as findings), not framework limits; shadcn/React would require rewriting every page. **Decision for V2: keep Blazor + Tailwind and fix the design layer first;** revisit React only if a multi-user web product or Blazor performance becomes a real constraint. This keeps the logic in services so the presentation layer stays replaceable.

---

## 27. MeridianPlan integration

### 27.1 What it is [DER — repository facts]
`wieslawsoltes/MeridianPlan` (https://wieslawsoltes.github.io/MeridianPlan/): a ~120 KB, dependency-free browser scheduler. **MIT licence** — copying and modifying into a private application is permitted provided the copyright and licence notice are retained. It disclaims any affiliation with Oracle/Primavera; V2 must not use those names or branding.
- **Stack:** plain JavaScript ES modules, HTML, CSS; **WebGPU** Gantt with Canvas 2D fallback; schedule computed in a **Web Worker**; **IndexedDB** persistence (localStorage fallback). Tooling: Node ≥ 20 (`node build.mjs`, `node --test`), Python Playwright smoke tests; deployed to GitHub Pages.
- **Layout:** `src/core/` (`calendar.js`, `model.js`, `scheduler.js`, `resources.js`, `storage.js`, `sample.js`), `src/app.js` (~105 KB, the whole UI), `src/ui/gantt.js`, `src/ui/icons.js`; `docs/` (ENGINE.md, ARCHITECTURE.md, SEMANTICS.md, VALIDATION.md); `tests/`, `examples/`, `scripts/`, `build.mjs`, `server.mjs`.
- **Features:** CPM forward/backward passes with total and free float, critical path and cycle diagnostics; FS/SS/FF/SF with lag; milestones and date constraints; calculation traces; incremental recompute; calendars with weekly shifts, split days, holidays and overrides (integer civil minutes, DST-safe); retained-logic progress (original/remaining duration, physical %); resource pools, assignments, actual hours, daily capacity/demand, overload detection, weekly utilisation, hourly-rate cost; immutable **baselines** and independent **scenario snapshots** with comparisons; WBS hierarchy, inline editing, Gantt drag/resize/link, transactional undo/redo; JSON (lossless) / CSV / resource-demand CSV import-export and printable HTML reports.
- **Not supported:** MS Project (MPP/XML) and P6 (XER/XML) interchange, cross-project dependencies, resource levelling, multi-user/server, authentication.
*(This summary comes from the repository metadata, README and file layout; the individual `src/core` files have not yet been read line by line. Reading `docs/ENGINE.md` and `tests/engine.test.js` is step 1 of the port.)*

### 27.2 What "copy all of it into EOS" means in practice [PRO]
1. **Vendor** the repository into `third_party/meridianplan/` with its `LICENSE` and a `NOTICE` file recording origin, commit and modifications.
2. **UI:** build the MeridianPlan UI bundle and host it as the **Project Plan** and **Combined Gantt** components inside EOS (an `<iframe>`/ES-module host that talks to EOS through a small adapter), replacing IndexedDB storage with the EOS API (server is the source of truth).
3. **Engine:** port `src/core` (calendar, scheduler, resources) to **C#** (`EOS.Scheduling`) using `docs/ENGINE.md` as the specification and the repository's test vectors plus the S3 golden tests (§15.8) as acceptance. The server, CLI and MCP then compute schedules and what-if with no browser.
4. **Parity tests:** run the same input plans through the JS engine and the C# engine in CI and compare dates/float/critical flags (a *differential test*) until the JS worker is only used for instant local feedback.
5. **Extend** (what MeridianPlan lacks, and EOS needs): multi-project portfolio view, effort-driven assignments across projects, the what-if engine (§15), skill-aware factors (§9.6), tracker importers, level-1 roll-up (§16), engineer swim-lanes and load strip, MS Project XML import (later), and multi-user server persistence.

### 27.3 Risks specific to MeridianPlan [DER]
Small, young project (11 stars, last push 6 Sep 2026): treat it as **vendored code we own** after import, not a dependency we follow upstream. `app.js` is a single 105 KB file: plan time to modularise before extending. WebGPU availability in WebView2/browsers varies: the Canvas fallback must stay tested.

---

## 28. Testing and acceptance

### 28.1 Golden scheduling tests (from S3, §15.8) — **must pass exactly**
| Test | Expected |
|---|---|
| G1 Baseline | P1 end 18 Nov; P2 end 16 Dec; P3 end 7 Nov; every task start/finish per §15.8; P1 critical path 1.1→1.3→1.4→1.5; 1.2 float 6 |
| G2 Scenario 1 | Mahesh → P2 on 26 Oct, no replacement: 1.2 → 7 Nov, 1.4 → 14 Nov, 1.5 → 28 Nov, P1 end 28 Nov (+9); 2.2 → 11 Nov, 2.3 → 23 Nov, 2.4 → 25 Nov, P2 end 25 Nov (−18) |
| G3 Alternative | Mahesh → P2 on 19 Nov: P1 unchanged; P2 end 3 Dec (−11) |
| G4 Release analysis | Twinkle 26 Oct / cost 0; Dharati earliest 6 Nov, +10 if 26 Oct; Sagar earliest 9 Nov, +7 if 26 Oct; Mahesh and Lalji earliest 19 Nov, +9; Priyanka not eligible |
| G5 Push plan | Twinkle 26 Oct + Dharati 6 Nov + Sagar 9 Nov → P2 end 18 Nov (−24); P1/P3 unchanged |
| G6 Single move | each single move of an eligible engineer on 26 Oct → P2 −18 |
Additional engine tests: dependency types and lags, constraints, calendars/holidays, cycle detection, partial %, replacement with ramp-up (Realistic profile), fixed-duration tasks, over-assignment reporting, incremental recompute equals full recompute, JS-vs-C# differential tests.

### 28.2 Data tests
Import counts (§24.4); idempotent re-import; alias resolution; sample-space isolation; credential-skip test (§24.9); diff/preview correctness.

### 28.3 API/CLI/MCP tests
Contract tests from the OpenAPI document; **CLI parity** (§25.7); MCP tool schema tests; audit-log presence on every write; auth required on every endpoint.

### 28.4 UI tests
Playwright flows: import → resolve aliases → open Command Center → open combined Gantt → run a move scenario → approve → see baseline promoted; screenshot checks for the design rules (§21.3); keyboard navigation; reduced-motion; both colour schemes.

### 28.5 Performance and resilience
Scenario latency and Gantt frame-rate budgets (§25.10); kill-and-restart server mid-import (no corruption); restore-from-backup drill.

### 28.6 Acceptance by module (done when…)
| Module | Done when |
|---|---|
| People & skills | All S2/S7/S6 data imported; skill summary matches §9.4.2; Know % matches §10.4 |
| Projects & plans | 454 portfolio + 11 live projects; tracker plans import with aliases resolved ≥ 95% or listed |
| Gantt | Combined view (L1) of live projects; baseline vs forecast; engineer lanes; export |
| Capacity | Load view flags the over-allocations in §8.3 |
| What-if | G1–G6 pass; scenario compare and approve/apply works |
| Movements | Ledger replays allocation history for any date |
| Infrastructure | 31 VMs imported with no credentials; reclaim list generated |
| Reports | All reports in §20 render with sample data; empty states correct |
| Platform | Runs as a service on the VM; reachable on Tailscale and LAN; nightly backup + restore drill passed |

---

## 29. Delivery roadmap

Sizes: **S** ≈ days, **M** ≈ 1–2 weeks, **L** ≈ 3+ weeks of focused work. Phases overlap where noted. Order follows dependency and risk.

| Phase | Scope | Size | Exit criteria |
|---|---|---|---|
| **0. Foundations** | Review this specification; resolve the high-impact open questions (§30: Q1, Q2, Q4, Q11, Q13); new `EOS_V2` repo; vendor MeridianPlan; CI skeleton on both runners | S | Spec signed off; repo + CI green |
| **1. Platform skeleton** | Server host, auth tokens, audit, SQLite + migrations, OpenAPI, `eos` CLI client + parity test, `/health`, systemd deploy to the VM, nightly backup timer | M | `eos person list` works against the VM over Tailscale; backup/restore drill passes |
| **2. People, skills, training** | Entities §7.1–7.2, imports I1–I4, I9–I11, alias screen, skill summary, training dashboard, doubts | M | §28.6 People & skills |
| **3. Scheduling engine** | C# port of calendar/scheduler/resources; golden tests G1; differential tests vs JS | L | G1 + engine unit tests green |
| **4. Projects, plans, Gantt** | Entities §7.3–7.4, imports I12–I15, project plan UI (MeridianPlan), combined Gantt (L1), health computation | L | §28.6 Projects & plans, Gantt |
| **5. Capacity, demand, allocation** | Entities §7.5, capacity/engineer-load views, demand matching, pipeline, forward supply–demand, imports I5–I8 | M | §28.6 Capacity |
| **6. What-if and movements** | Scenario engine (types A–H), release analysis, compare, approval workflow, movement ledger, golden G2–G6 | L | §28.6 What-if, Movements |
| **7. Infrastructure** | Entities §7.6, import I17 (no credentials), project–VM links, capacity, reclaim list, alerts | S–M | §28.6 Infrastructure |
| **8. Reports and Command Center** | §20 report catalogue, §21 dashboards, §22 alert engine | L | §28.6 Reports |
| **9. AMC, closure, V1 integration** | AMC ticket flow, closure checklist, move V1 performance/reviews under Person, `Analytics` extraction, MCP completion | M | Feature parity with V1 + MCP |
| **10. Hardening** | Performance budgets, accessibility pass, restore drills, documentation, handover | M | §25.10 met |

**Use of the two runners:** Linux jobs (build, unit, API, engine) on `ubuntusb-shell`; Windows jobs (Playwright with WebView2/Edge, WPF shell build if kept) on the **10.2.6.42** runner.
**Coordination point [OPEN Q13]:** the Operational Agility document states an engineer has been assigned part-time to build the software solution. EOS V2 and that effort must be reconciled so SSM does not build two tools for the same problem.

---

## 30. Open questions and assumptions register

| # | Question / assumption | Why it matters | Proposed default | Owner |
|---|---|---|---|---|
| Q1 | ~~Hours model~~ **Resolved 14 Oct: all hours and calendars are configurable** (§8.7). Remaining decision: which values to *seed* (160 h/month vs Mon–Sat 8 h) | Seed data only | Seed 160 h/month fixed capacity *and* a Mon–Sat 8 h calendar; leads pick per team | Owner |
| Q2 | Level-1 task definition for the master schedule (§16.2) | Drives the combined Gantt | Module = L1, task = L2 | Owner |
| Q3 | Expansion of **LPP**, **TAC**, **DOW** | Naming | Store as given | Owner |
| Q4 | ~~Progression~~ **Resolved 14 Oct: manual** (§9.5). Remaining: names of levels for module proficiency (S6 has only Beginner/Expert) | Labels | Reuse the 1–5 names; level count is configurable | Owner |
| Q5 | Single vocabulary for knowledge bands (§10.5) | Labels | Configurable bands | Owner |
| Q6 | Is a "Need to discuss" cell a doubt? (§10.6) | Doubt counts | Yes — auto-create a doubt | Training owner |
| Q7 | Meaning of ERP status codes CLS/RLS/TECO/FUTURE ORDER/HLD (§11.2) | Portfolio filters | Closed / Released (active) / Technically complete / Future order / On hold | Owner |
| Q8 | Health thresholds (§11.6) | At-risk detection | As proposed | Owner |
| Q9 | Meaning of *IKOM* in the ERP export (§19.2) | Kick-off tracking | Internal kick-off | Owner |
| Q10 | Ramp-up and skill factors (§9.6) | Realistic what-if | Compat default; Realistic optional | Owner |
| Q11 | Confirm S3's working rules (S3 says "to be confirmed"): 8 h, Mon–Sat, fixed work, no-replacement rule, full-speed joiners | Engine behaviour | As written | Owner |
| Q12 | Live VM state feed (Hyper-V scripts, monitoring exports) (§18.4) | Automation | Manual import first | Infra owner |
| Q13 | ~~Reconcile with the engineer building the Excel→software tool~~ **Closed 14 Oct: the owner's team is building EOS V2 itself.** | — | — | — |
| Q21 | **ERP WBS/task export:** what format or API exposes project WBS/tasks (the current ERP export is project-level only)? (§16.7) | Needed for "import from ERP, then add tasks" | Build the importer against the tracker format first; add ERP when a sample export exists | Owner |
| Q14 | Source of leave and timesheet data | Capacity actuals | Import `LV_LeaveSummaryforRP` and utilisation reports | Owner |
| Q15 | Meaning of "ASD STE 100" (S4) | Documentation standard | Simplified Technical English (ASD-STE100) | Owner |
| Q16 | Which team does each of Safwan, Krinav, Priyen, Udaykumar belong to | Team capacity | Unassigned until set | Leads |
| Q17 | ~~Who approves what~~ **Resolved 21 Oct: the owner approves everything; nobody else influences decisions** (principle 15) | — | — | — |
| Q18 | Retention rules for personal/performance data | Governance | Keep; export on request | Owner |
| Q19 | Customer-facing documents (contractual schedules) ↔ internal plans | Scope | Out of scope; link only | Owner |
| Q20 | Do spares/service projects (Service+Spare, Mandays) need engineer allocation? | Capacity | Mandays yes; others optional | Owner |

---

## 31. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Scope too large for one build; stalls before value | High | High | Phase 1–4 deliver a usable Gantt and people view first; what-if is phase 6 but the engine is phase 3 |
| R2 | Sample data mistaken for real data | Medium | High | Sample space; unverified flags (§24.1) |
| R3 | What-if numbers trusted without real work estimates (trackers have durations only) | High | High | "Estimated" flag; lower-confidence badge; encourage real estimates (§16.6) |
| R4 | ~~Two tools for one problem~~ closed (Q13) | — | — | — |
| R14 | "Everything configurable" multiplies states to test and can make the system hard to reason about | Medium | Medium | Seed sensible defaults; settings have schemas/validation; every result records its settings snapshot; a *Settings diff* view; golden tests run on the default profile and on at least one alternative profile |
| R5 | Engine divergence between JS (UI) and C# (server) | Medium | Medium | Differential tests; server is the source of truth |
| R6 | Skill ratings gamed or stale | Medium | Medium | Evidence blend; stale flags; disagreement alert |
| R7 | Sensitive data exposure (no firewall on VM; performance data) | Medium | High | Bind to Tailscale/LAN only; per-person tokens; audit; consider enabling `ufw` |
| R8 | Single-disk VM, no VM backup | Medium | High | Nightly SQLite backup with off-VM copy; restore drills |
| R9 | CI runner concurrency = 1 starves other jobs | Medium | Low | Schedule heavy jobs off-hours; use the Windows runner for UI tests |
| R10 | MeridianPlan is small/young; upstream may vanish | Medium | Low | Vendored copy under our control; C# engine independent |
| R11 | Alias mapping errors misattribute work | Medium | Medium | Review screen; confidence flags; no silent merges |
| R12 | Over-fitting to the current 10 skills/132 modules | Low | Medium | Skills/modules are data; add without code changes |
| R13 | Plaintext credentials in source workbooks leak through the system | Medium | High | Credential-skip importer + test (§24.9); recommend a secret store |

---

## Appendix A — Enumerations (as found in the sources)

| Domain | Values |
|---|---|
| Rating scale | 1 No knowledge · 2 Basic · 3 Intermediate · 4 Advanced · 5 Expert |
| Module state | Know · don't Know · Need to discuss |
| Module proficiency (S6; mostly unset) | Beginner · … · Expert (intermediate names undefined) |
| Doubt result | Solved · Assign · Hold (blank = unassigned) |
| Tracker project status | Entered · Under Development · Hold · Deployed · Completed |
| Task status | Completed · In Progress · Pending / Future · Delayed |
| Project health | On Track · Moderate Risk · At Risk |
| ERP status | CLS · RLS · TECO · FUTURE ORDER · HLD |
| ERP project type | Project and Project Services · Service+Spare · Mandays · Runrate |
| Allocation status | Confirmed · Proposed |
| Demand status | Filled · Partially Filled · Gap – Escalated · Open |
| Opportunity status / stage | Expected / Qualified · Proposal Sent · Negotiation |
| Log type / gap type | Capacity Gap · Change Request · Decision / Proficiency mismatch |
| VM state | Running · Off (plus Unknown · No access in V2) |
| Team | LPP Engineering · LPP GET · LPP TAC · Run-rate Engineering · Independent |
| Role titles | Engineer · Senior Engineer · Lead Engineer · Graduate Engineer Trainee |
| Location | Office · Site · Remote |
| Product lines | DOW · MES (X-MES, X-Batch) · Historian · LIMS · CMS · PIP · EMS · AIMS · BATCH |

## Appendix B — Formula index

| # | Formula | Section |
|---|---|---|
| F1 | Available hrs = availability % × standard hrs (160) | §8.1 |
| F2 | Planned hrs = available × target utilisation (0.85) | §8.1 |
| F3 | Load vs planned / vs available | §8.2 |
| F4 | Skill resources = count(rating ≥ 2); deployable = count(rating ≥ 3) | §9.3 |
| F5 | Competency % = average rating ÷ 5 | §9.4.1 |
| F6 | Gap vs required = competency % − required % | §9.4.1 |
| F7 | Evidence rating = 1 + 4 × (weighted module Know share) | §9.4.3 |
| F8 | Blended rating = α·lead + (1−α)·evidence | §9.4.3 |
| F9 | Know % = Know ÷ total modules | §10.3 |
| F10 | Weighted hours = hours × win probability | §12.4 |
| F11 | Demand fill = supplied h/week ÷ required h/week | §12.2 |
| F12 | Duration (working days) = ceil(work ÷ (hours/day × Σ allocation × factor)) | §14.3 |
| F13 | Work done = Σ working-day hours × allocation × skill factor × experience factor | §14.7 |
| F14 | Total float = LS − ES; critical ⇔ float ≤ 0 | §14.5 |
| F15 | Slip/gain = scenario finish − baseline finish (working days) | §15.6 |
| F16 | L1 progress = Σ(work × progress) ÷ Σ work | §16.3 |
| F17 | Health thresholds (schedule variance, delay ratio, progress gap) | §11.6 |
| F18 | Days to planned completion = planned − last updated (calendar days) | §11.5 |

## Appendix C — Source column dictionaries (abbreviated)

**S2 `Engineers`:** Emp ID · Full Name · Team · Role · Base Location · Availability % · Available Hrs/Month · Planned Hrs/Month · Status · Remarks.
**S2 `Skill_Matrix`:** Emp ID · Full Name · Team · 10 skill columns · Avg Rating · Skills at Deployable Level · Assessed Date · Assessed By.
**S2 `Running_Projects`:** Project ID · Project Name · Project Lead · Expected Order Value (lakh) · Expected Hours · Booking Month · Duration (months) · monthly hours · totals/check · status/priority.
**S2 `Upcoming_Projects`:** as above + Likely Booking Month · Status · Stage · Win Probability · Required skills · Weighted hours.
**S2 `Project_Demands`:** Demand ID · Project ID · Project Name · Skill Required · Min Level · Hrs/Week · Start · End · Location · Eligible Engineers · Best-rated Engineer · Demand Status.
**S2 `Resource_Allocation`:** Engineer · Team · Project ID · Demand ID · Role on Project · Allocation Status · monthly hours.
**S2 `Change_Gap_Log`:** Log ID · Type · Project ID · Demand ID · Raised By · Date Raised · Description · Gap Type · Impact (Hrs) · Impact (Days) · Option/Decision · Approver · Decision Date · Status.
**S6 `Module`/`Module Proficiency Level`:** Sr No · Product · Module · Sub Module · one column per engineer. **`Need to Discuss`:** Sr No · Module · Sub Module · Doubt in Detail · Doubt date · Raise Engineer · Assign Date · Assign Engineer · Solved Date · Result · Remarks.
**S8 `LPP`:** P. No · Name · Product Name · Status · Project Name · Technical Manager · PM · Assigned Engineer(s) · Notes/Remarks. **`AMC`:** see §11.8. **Project sheets:** §11.4.
**S9 `ProjectList`:** see §11.2. **`Sheet2`:** Project No · Project Name · Product · Project Manager · Technical Manager.
**S10 `Sheet1`:** Host IP · VM IP · Virtual Machine · State · Project/Purpose · Assigned To · Need Backup · isdelete VM · VM Removed. **Physical server sheets:** Sr No · Status · Server Type · IP Address · Used For · *(credential columns skipped)* · HDD/C/D/E/F drive remaining/total · Total RAM · CPU cores · Logical processors.

## Appendix D — Requirement traceability

| Requirement (source) | Specified in |
|---|---|
| Track projects, engineers assigned (user) | §11, §13, §16, §17 |
| What-if: move engineer or skill (user, S3, S5) | §15 |
| Combined Gantt of all projects (user) | §16 |
| VMs for projects engineers work on (user) | §18 |
| Skillset and training (user, S4, S6, S7) | §9, §10 |
| Entire PM flow, reports (user) | §19, §20 |
| Command center (user) | §21 |
| CLI + API + MCP + web UI; always-on server on the VM (user) | §25 |
| Teams and team leaders; everything visible; ≤ 4 users (user) | §23 |
| MeridianPlan copied into EOS (user) | §27 |
| Available hours / Planned hours (N1, N2) | §8 |
| Skill rating formulae (N3) | §9.4 |
| Skill in assignment and movement (N4) | §9.6, §15.5 |
| Parallel projects (N5) | §13.3 |
| Upcoming projects (N6) | §12.4–12.5 |
| Same-skill tie-break (N7) | §9.7 |
| Tracking resource movements (N8) | §17 |
| Separate WBS per project (N9) / Master = L1 (N10) | §16 |
| ASD STE 100 (N11) | §3, §30 Q15 |
| Skill / training transactions (N12, N13) | §9.5, §10.6 |
| Interconnect skills and scenarios (N14) | §9.8, §15.5 |
| What-if movement, no limit (N15, N16) | §15.2–15.3, §15.6.1 |
| Work done = time × factor (N17) | §9.6, §14.7 |
| Don't tie tasks to engineers after completion (S5) | §13.6 |
| Sample projects, MS Project behaviour (S3, S5) | §14, §15.8, §28.1 |

## Appendix E — Document history
| Version | Date | Change |
|---|---|---|
| Draft 1 | 14 Oct 2026 | First complete compilation from S1–S10 and the 14 Oct meeting notes/chats. Scheduling examples re-computed and confirmed. |
| Draft 2 | 14 Oct 2026 | Owner decisions: everything configurable (§4 #11, §8.7); WBS authored in-system or imported then extended (§16.7, Q21); skill progression manual (§9.5); the team building EOS V2 is the owner's (Q13 closed). |
