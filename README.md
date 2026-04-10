# Overseer

Overseer je moderní SaaS webová aplikace pro:
- monitoring dostupnosti serverů,
- evidenci a vyhodnocení bezpečnostních událostí,
- kontrolu compliance Windows Server licencí (per-core, RDS CAL),
- alerting e-mailem,
- monetizaci přes trial/subscription/enterprise tarify.

## Tech stack
- **Backend:** ASP.NET 9 Minimal API + EF Core + PostgreSQL
- **Frontend:** React + TypeScript + Vite
- **DB:** PostgreSQL
- **Infra:** Docker + docker-compose

---

## 1) Předpoklady (lokální vývoj bez Dockeru)

Nainstalujte:
- .NET SDK **9.0.100+**
- Node.js **22+** + npm
- PostgreSQL **16+** nebo **17**

Kontrola:
```bash
dotnet --info
node -v
npm -v
psql --version
```

---

## 2) Rychlý start přes Docker (doporučeno)

```bash
docker compose up --build
```

Služby:
- Frontend: `http://localhost:5173`
- API Swagger: `http://localhost:8080/swagger`
- API Health: `http://localhost:8080/health`
- Mailhog UI: `http://localhost:8025`

Stop:
```bash
docker compose down --remove-orphans
```

---

## 3) Lokální start bez Dockeru

### Backend
1. Vytvořte databázi a uživatele v PostgreSQL.
2. Upravte `backend/Overseer.Api/appsettings.json` nebo použijte environment proměnnou `ConnectionStrings__Postgres`.
3. Spuštění:
```bash
dotnet restore backend/Overseer.Api/Overseer.Api.csproj
dotnet run --project backend/Overseer.Api/Overseer.Api.csproj
```

### Frontend
```bash
npm ci --prefix frontend
npm run dev --prefix frontend
```

Volitelné env:
- `VITE_API_URL` (default `http://localhost:8080`)
- `VITE_API_KEY` (pro autentizované endpointy)

---

## 4) Build a test postup (kompletní návod)

### A) Build backendu
```bash
dotnet build backend/Overseer.Api/Overseer.Api.csproj
```

### B) Build frontendu
```bash
npm ci --prefix frontend
npm run build --prefix frontend
```

### C) Smoke test API
```bash
curl -fsS http://localhost:8080/health
curl -fsS http://localhost:8080/api/public/pricing
```

### D) End-to-end minimální flow (ručně)
1. Vytvořte tenant:
```bash
curl -sS -X POST http://localhost:8080/api/tenants \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme","contactEmail":"ops@acme.test"}'
```
2. Uložte vrácený `apiKey`.
3. Vytvořte server node:
```bash
curl -sS -X POST http://localhost:8080/api/servers \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"name":"Web-01","hostname":"web01.internal","port":443}'
```
4. Pošlete availability check (výpadek):
```bash
curl -sS -X POST http://localhost:8080/api/availability-checks \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"serverNodeId":"<SERVER_ID>","isUp":false,"responseTimeMs":0,"statusCode":503,"errorMessage":"connection timeout"}'
```
5. Zapište security event:
```bash
curl -sS -X POST http://localhost:8080/api/security-events \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"source":"EDR","severity":"high","description":"Ransomware pattern detected"}'
```
6. Zapište licenční compliance:
```bash
curl -sS -X POST http://localhost:8080/api/license-compliance \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"windowsServerCoreLicenses":32,"requiredCoreLicenses":40,"rdsCalAssigned":50,"rdsCalRequired":80}'
```
7. Checkout tarifu:
```bash
curl -sS -X POST http://localhost:8080/api/billing/checkout \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"planType":1}'
```

---

## 5) Bezpečnost
- API chráněné API key middlewarem (`X-Overseer-ApiKey`).
- API klíč se ukládá pouze hashovaný (BCrypt).
- Všechny vstupní DTO jsou validované FluentValidation.
- CORS je explicitně povolené jen pro `http://localhost:5173`.
- Žádná citlivá data nejsou hard-coded ve zdrojáku (kromě lokálních dev hodnot v `appsettings.json`).

---

## 6) Monetizace
- **Trial:** 14 dní zdarma, 5 serverů.
- **Subscription:** $49 / měsíc, 50 serverů, SIEM webhook ingest, SLA reporty.
- **Enterprise:** $299 / měsíc, unlimited, SSO/SAML-ready, custom exporty.

Platební workflow je připravené přes `IPaymentService` (aktuálně mock provider), aby šel snadno přepnout např. na Stripe.

---

## 7) Nice-to-have připravené v návrhu
- SLA reporting a trendy.
- Audit trail.
- SIEM integrace.
- Enterprise SSO/SAML rozšiřitelnost.

