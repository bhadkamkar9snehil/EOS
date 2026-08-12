# Engineering Execution Discipline

The product turns operational rules and system evidence into auditable engineering execution outcomes without collapsing distinct behaviours into a single opaque score.

## Language

**Directive**:
A rule describing what must happen, who it applies to, what evidence satisfies it, and when it is due.
_Avoid_: Metric, score, policy flag

**Population**:
The engineers to whom a directive applies for a defined period or triggering event.
_Avoid_: Audience, employee list

**Obligation**:
One person-specific instance of a directive with its own trigger, due time, and outcome.
_Avoid_: Compliance row, task

**Evidence**:
A source-backed fact showing what happened and when, such as the latest work-log filled timestamp for an accountable day.
_Avoid_: Manual pass, inferred note

**Outcome**:
The evaluated state of an obligation: Pending, On Time, Late, Overdue, Excused, Not Applicable, or Waived.
_Avoid_: Compliance score, pass/fail

**Participation**:
The share of obligations completed, whether on time or late.
_Avoid_: Punctuality

**Punctuality**:
The share of completed obligations that were completed on time.
_Avoid_: Participation

**Exception**:
An explicit, auditable reason that changes normal obligation evaluation to Excused, Not Applicable, or Waived.
_Avoid_: Override, ignore flag
