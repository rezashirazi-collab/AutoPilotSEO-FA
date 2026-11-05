# سئو خودران (autopilot_seo)

Autonomous SEO SaaS: reads Search Console + crawl data, picks the next most valuable SEO action, prepares it, executes on WordPress only after approval, and measures impact. Product spec: the design doc «سند طراحی محصول سئو خودران» (kept outside the repo); phased plan: `docs/PLAN.md`; local dev notes: `docs/DEV.md`.

## Stack
- .NET 10, ASP.NET Core Razor Pages, EF Core 10 + SQL Server (dev server is SQL 2019 → `UseCompatibilityLevel(150)`), ASP.NET Identity.
- `src/AutopilotSeo.Core` (entities, DbContext, rules/algorithms, migrations), `src/AutopilotSeo.Web` (site + panel), `tests/AutopilotSeo.Tests` (xUnit).
- Connection string only in user-secrets of the Web project. Never commit it.

## UI
- Persian, RTL (`<html lang="fa" dir="rtl">`). Persian digits and Shamsi dates via `Fa` helpers (`Fa.Num`, `Fa.N`, `Fa.Date`).
- Hand-written CSS in `wwwroot/css/site.css`, no CSS/JS frameworks. Self-hosted Estedad font (no CDNs: users are in Iran).
- Strong colors are reserved for risk levels 0–3 (`--r0..--r3`); primary buttons are ink (`--ink`).
- Product name lives in one place: `Fa.ProductName`.

## Rules from the spec that code must keep
- No recommendation before the business profile passes the gate (`ProfileRules.Evaluate`). Approved profile versions are frozen; edits create a new version.
- `AuditEvents` is append-only (DB trigger). Log with `db.Log(...)` in the same SaveChanges as the change.
- Every site write goes through one executor (snapshot → staleness check → write → re-read → ledger). Level 3 changes are never automatic.
- Red lines are programmatic filters, not prompt text.
