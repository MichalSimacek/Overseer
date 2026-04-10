# Architecture

## Backend (ASP.NET 9 Minimal API)
- API key middleware pro tenant scope.
- EF Core + PostgreSQL (`OverseerDbContext`).
- FluentValidation pro vstupy.
- Stripe checkout (`StripePaymentService`).
- Audit log service + SIEM webhook service.

## Klíčové endpointy
- Onboarding: `POST /api/tenants`
- Servery: `POST /api/servers`
- Dostupnost: `POST /api/availability-checks`
- Security eventy: `POST /api/security-events`
- Licence: `POST /api/license-compliance`
- Alert rules: `POST /api/alerts`
- SIEM: `PUT /api/integrations/siem`
- SSO/SAML: `PUT /api/identity/sso-saml`
- SLA report: `GET /api/reports/sla?days=30`
- Audit log: `GET /api/audit-logs`
- Billing: `POST /api/billing/checkout`
- Public pricing: `GET /api/public/pricing`

## Frontend
- React + TypeScript + Vite.
- Jedna jednoduchá dashboard stránka, pricing + checkout flow.

## Bezpečnost
- API klíče hashované přes BCrypt.
- Tenant data izolace na úrovni endpointů.
- CORS omezený na lokální frontend origin.
