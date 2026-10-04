# Security Policy

## Project status & threat model

Dataverse AI Forge is a **local, single-user desktop app**. The backend binds to
`127.0.0.1` and is not meant to be exposed on a network. Under that model the
security surface is small, and what matters is already in place:

- **CORS is origin-locked** to the app (`Cors:AllowedOrigins`, default
  `http://localhost:3000`). This blocks the only real desktop vector: a malicious
  website POSTing to the local backend fails its preflight. (`AllowAnyHeader` /
  `AllowAnyMethod` is fine here; the origin is the gate.)
- **`/api/agui` and `/api/settings/test` are rate-limited** (runaway-token-cost
  guard and live-credential-probe guard).
- **Secrets are AES-GCM encrypted at rest** in `user-settings.json` (readable only
  by processes running as your OS user), and an **opt-in bearer token**
  (`Auth:SharedSecret`) exists if the backend is ever fronted.

Because the app runs on your machine as you, treat the data folder (`state.db`,
`user-settings.json`, `.secret.key`) as you would any local credential store.

The agents can **create and delete** Dataverse metadata (tables, columns, forms,
views, security roles, …). Always point the app at a **throwaway developer / trial
org**, never production.

**Do not** expose the backend to a network, run it on a shared or multi-user host,
or point it at a production Dataverse environment. If you ever change that model,
revisit the posture: tighten CORS headers/methods, auto-provision the bearer token,
enforce HTTPS, add request-size limits, and move the encryption key into an OS
keychain (macOS Keychain / Windows DPAPI) instead of the on-disk `.secret.key`.

## Supported versions

This is an example project; only the `main` branch is maintained. There are no
long-term-support branches.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security problems.

- Preferred: use **GitHub's private vulnerability reporting**, via the repository's
  **Security > Advisories > Report a vulnerability** tab.
- Include steps to reproduce, the affected commit/version, and the impact.

Because this is a volunteer / example project there is no formal SLA, but credible
reports that affect the default local posture will be prioritized. Thanks for
helping keep users safe.
