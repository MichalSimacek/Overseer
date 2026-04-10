# Architekturální přehled

## Backend
- `Program.cs`: API endpointy, registrace služeb, CORS, validace a middleware pipeline.
- `Domain/Entities.cs`: doménové entity + plánové modely.
- `Infrastructure/OverseerDbContext.cs`: datový model + indexy.
- `Common/`: middleware a služby (API key, e-mail, pricing, payment).

## Frontend
- Jednoduchý dashboard + pricing + checkout call.
- Důraz na čistotu, minimum komplexity, snadné rozšíření.

## Data model
- `Tenant` reprezentuje zákazníka/plán.
- `ApiClient` drží hash API klíče.
- `ServerNode`, `AvailabilityCheck`, `SecurityEvent`, `LicenseComplianceRecord`, `AlertRule`.
- `SubscriptionOrder` drží billing audit.

## API capabilities (MVP)
- Tenant onboarding (`/api/tenants`).
- Monitoring dostupnosti (`/api/availability-checks`).
- Security incident ingest (`/api/security-events`).
- License compliance ingest (`/api/license-compliance`).
- Alert rules (`/api/alerts`).
- Dashboard (`/api/dashboard`).
- Pricing + checkout (`/api/public/pricing`, `/api/billing/checkout`).

## Multi-tenant bezpečnost
- Tenant kontext je odvozený z API klíče.
- Každý endpoint pracuje jen s daty tenant scope.
- API key je ukládán pouze hashovaný (BCrypt).
