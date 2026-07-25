# Security Policy

## Project status & threat model

Dataverse AI Forge is a **localhost-first developer tool and reference example**, not a
hardened multi-user product. By default it runs with:

- **no authentication** on the backend API (opt-in via `Auth:SharedSecret` — see
  `backend/src/DataverseForge.Api/appsettings.json` and [TODO.md](TODO.md)),
- credentials stored **on the local machine** in `user-settings.json`, **encrypted at
  rest** (AES-GCM) but readable by anything running as your OS user,
- CORS limited to `http://localhost:3000` with permissive headers/methods.

This is acceptable on `127.0.0.1` for solo development. **Do not**:

- expose the backend to a network,
- run it on a shared or multi-user host,
- point it at a **production** Dataverse environment.

The agents can **create and delete** Dataverse metadata (tables, columns, forms, views,
security roles, …). Always use a throwaway developer/trial org.

Known gaps and the roadmap to close them are tracked openly in [TODO.md](TODO.md)
(see the "TODO — Security" section).

## Supported versions

This is an example project; only the `main` branch is maintained. There are no
long-term-support branches.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security problems.

- Preferred: use **GitHub's private vulnerability reporting** — the repository's
  **Security → Advisories → Report a vulnerability** tab.
- Include steps to reproduce, the affected commit/version, and the impact.

Because this is a volunteer/example project there is no formal SLA, but credible reports
that affect the default localhost posture will be prioritized. Thanks for helping keep
users safe.
