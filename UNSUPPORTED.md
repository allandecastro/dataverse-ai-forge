# Unsupported Capabilities

This app builds Dataverse solutions through chat, but a few capabilities are
intentionally out of scope. When a user asks for one of these, agents
**reject the request with a maker-portal pointer** rather than half-build it.

The list is short on purpose — the demo focuses on tables, fields, option
sets, alternate keys, forms, views, solutions, and reference/test data.
Everything else stays in the maker portal.

## Field types

| Type | Why it's out | What to suggest instead |
|---|---|---|
| **Calculated** | Dataverse Web API rejects `FormulaDefinition` on attribute create. We'd land an empty placeholder column the user has to finish in the portal anyway — half-finishing is worse than refusing. | Build the column in the maker portal: `Tables → <table> → New column → Calculated`. |
| **Formula (Power Fx)** | Power Fx columns require the visual formula editor; Web API exposes them only after they exist. | Maker portal: `New column → Formula`. |
| **Lookup via `plan_create_field`** | Dataverse forbids creating lookup attributes through the attribute API. | Use `plan_create_one_to_many_relationship` (single-target) / `plan_create_polymorphic_lookup` (multi-target) / `plan_create_customer_lookup` (Account+Contact) — each creates the relationship(s) + the lookup column atomically. |

## Component categories

| Capability | Why it's out |
|---|---|
| **Plugins / C# code generation** | Out of demo scope. Use the maker portal + Visual Studio. |
| ~~**Business rules**~~ | **Now partially supported (Phase 7)** — AutomationBuilder manages existing rules (rename / activate / deactivate / delete). Creation of new rules from scratch is deferred until XAML templates can be validated against a real Dataverse environment. Build new rules in the maker portal first, then manage them here. |
| **Power Automate flows / workflows** | Out of scope. Use the maker portal. |
| ~~**Model-driven app shell + sitemap authoring**~~ | **Now supported** (Phases 1-4) — UIBuilder owns appmodule CRUD + sitemap XML + component attach/detach + role assign/unassign + ValidateApp + PublishApp. |
| ~~**Dashboards**~~ | **Now supported** — UIBuilder owns org-owned dashboard CRUD (systemforms with type=0). |
| ~~**Business Process Flows (BPFs)**~~ | **Now partially supported (Phase 7)** — AutomationBuilder manages existing BPFs (rename / activate / deactivate / delete). Creation of new BPFs from scratch is deferred until XAML templates can be validated against a real Dataverse environment. Build new BPFs in the maker portal designer first, then manage them here. |
| **Custom APIs** | Maker portal. |
| **Field-level / column security profiles** (`fieldsecurityprofile` + `IsSecured` flag on columns) | Maker portal. Distinct from security roles — column-level secrecy needs its own object class and a UI builder integration we haven't designed. |
| **Pre-edit privilege snapshot for undo** | `execute_update_security_role` records `IsReversible=false` — the prior privilege matrix isn't captured. Use a manual rollback (plan a new update reversing the delta) or restore from a solution export. |
| ~~**Security roles**~~ | **Now supported** (Phase 6) — SecurityBuilder agent handles role create / update with privilege deltas / delete with safety pre-flight. Field-level security profiles remain out of scope. |
| **Documentation generation (data dictionary, ERD, changelog, README)** | Removed from this build. |
| **GitHub push for plugin code** | Removed from this build. |

## Relationship constraints (supported but tricky)

These do work, but with platform constraints worth surfacing up front:

- **Parental relationships**: each child entity can have at MOST ONE parental
  relationship (Dataverse rejects a second). Default to `Referential` unless
  the user explicitly wants cascading delete + assign + share + unshare from
  parent to child. Pass `lookupBehavior: "Referential"` to
  `plan_create_one_to_many_relationship` for the safe default.
- **Polymorphic / Customer lookups**: supported via dedicated tools
  (`plan_create_polymorphic_lookup`, `plan_create_customer_lookup`). They
  wrap Dataverse's atomic `CreatePolymorphicLookupAttribute` /
  `CreateCustomerRelationships` actions — the column + all per-target
  relationships land in a single round-trip. Don't split into N separate 1:N
  lookups; that produces a different shape.

## Why this list exists

When the model attempts an unsupported capability and the platform 4xx's, the
agent loops trying to "fix" the payload. Refusing at plan time with a clear
"build it in the portal" message saves the user a 2-minute thrash and gives
them an actionable next step.
