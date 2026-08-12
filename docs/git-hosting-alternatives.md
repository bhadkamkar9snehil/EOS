# Git Hosting Alternatives to GitHub

Context: you asked for alternatives to GitHub-hosted "cloud stuff" — free/self-hostable options,
in the same spirit as wanting GitHub Actions alternatives. This covers the hosting platform
itself (repos, issues, PRs), not CI specifically (see `tailwind-grid-ci-plan.md` §3 for that).

## Options

### Self-hosted (full control, your own infrastructure)

| Option | Cost | Notes |
|---|---|---|
| **Gitea** | Free, open-source | Lightweight (single Go binary, SQLite-capable — same DB engine this app already uses), low resource footprint, easy to run on a small VM or even a NAS. Has issues, PRs, wiki, basic Actions-compatible CI (Gitea Actions, optional). Mature, widely used for small-team self-hosting. |
| **Forgejo** | Free, open-source | A community-driven fork of Gitea (post-2023, in reaction to Gitea's corporate direction). Functionally near-identical to Gitea today, generally considered the more community-aligned choice if that matters to you. If starting fresh with no existing Gitea investment, Forgejo is the slightly better default pick. |
| **GitLab Community Edition (self-hosted)** | Free, open-source | Much heavier than Gitea/Forgejo (full DevOps platform: CI/CD, container registry, more). Justified only if you want built-in CI/CD and don't mind the extra resource/maintenance cost. Overkill for this project's current scale. |

Both Gitea and Forgejo can run in a single Docker container on nearly any always-on machine
(a home server, a small cloud VM, even a Raspberry Pi) and would give you full control with zero
recurring cost beyond whatever you're already paying for the host. Given this app already deals
in SQLite, a Gitea/Forgejo instance is a very light addition to your operational surface.

### Hosted, free tier, no self-hosting required

| Option | Cost | Notes |
|---|---:|---|
| **Codeberg** | Free | A nonprofit-run public Forgejo instance — closest "GitHub replacement" if you don't want to run your own server. Public repos only on the free tier (no private-repo hosting), which matters if this project needs to stay private. |
| **GitLab.com** | Free tier | Private repos included free (unlike Codeberg), plus free CI/CD minutes (400/month on free tier) if you ever want CI without running Actions or your own runner. Reasonable if you want "GitHub-equivalent but not GitHub" with zero hosting to manage. |
| **Bitbucket (Atlassian)** | Free tier | Private repos free for small teams (up to 5 users), built-in Pipelines CI with a free-minutes allowance. Worth mentioning only if you're already in the Atlassian ecosystem (Jira etc.) — no strong reason to pick it otherwise. |
| **SourceHut** | Free/pay-what-you-want | Minimalist, email/CLI-driven workflow (no web PR UI in the GitHub sense) — a poor fit if you want the current PR-review workflow to keep working the way it does today. |

## Recommendation

- If the goal is genuinely "get off GitHub, self-hosted, free": **Forgejo**, self-hosted on a
  small VM/always-on machine — light footprint, GitHub-like UI/workflow (PRs, issues, code
  review), and it can *also* run Gitea Actions as an Actions-compatible CI option if you decide
  you want automation later without going back to GitHub Actions specifically.
- If the goal is "off GitHub, but don't want to run a server": **GitLab.com** free tier is the
  closest drop-in (private repos + free CI minutes included), with Codeberg as the nonprofit
  alternative if the repo can be public.
- If this project stays on GitHub for now (which is a perfectly reasonable choice — nothing about
  your current setup requires migrating), the recommendation from the CI-alternatives plan still
  stands: skip Actions, use a local release script or Azure DevOps Pipelines for automation.

This is evaluation only — no migration was performed. Say the word if you want a concrete
migration plan (mirroring history, redirecting the git remote, moving open issues/PRs) for
whichever target you pick.
