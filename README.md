# Overseer

Jednoduchý a moderní SaaS pro:
- monitoring dostupnosti serverů,
- bezpečnostní události,
- kontrolu Windows Server licencí (core + RDS),
- alerty do e-mailu,
- monetizaci (Trial / Subscription / Enterprise).

## Tarify
- **Trial**: 14 dní zdarma, 5 serverů.
- **Subscription**: **$49 / měsíc, 20 serverů**, SIEM webhook ingest, SLA reporty.
- **Enterprise**: $299 / měsíc, neomezeně serverů, SSO/SAML, priorita podpory.

## Co je hotové (včetně "nice-to-have")
- ✅ SLA reporting (`GET /api/reports/sla?days=30`)
- ✅ Audit trail (`GET /api/audit-logs`)
- ✅ SIEM integrace (`PUT /api/integrations/siem`)
- ✅ Enterprise SSO/SAML konfigurace (`PUT /api/identity/sso-saml`)

---

## Stručný návod krok po kroku (pro úplné začátečníky)

### 0) Co musíš mít nainstalované
- Docker Desktop
- Git

Volitelné (pokud chceš běžet bez Dockeru):
- .NET SDK 9
- Node.js 22+
- PostgreSQL

---

### 1) Naklonuj repo
```bash
git clone <TVE_REPO_URL>
cd Overseer
```

### 2) Nastav Stripe (důležité)
V `backend/Overseer.Api/appsettings.json` doplň:
- `Payments:Stripe:PublishableKey`
- `Payments:Stripe:SecretKey`
- `Payments:Stripe:SuccessUrl`
- `Payments:Stripe:CancelUrl`

Pro lokální test můžeš použít Stripe test klíče (`pk_test_...`, `sk_test_...`).

### 3) Spusť vše jedním příkazem
```bash
docker compose up --build
```

### 4) Otevři aplikaci
- Frontend: http://localhost:5173
- Swagger: http://localhost:8080/swagger
- Mailhog (náhled alert e-mailů): http://localhost:8025

### 5) Ověř, že backend běží
```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/public/pricing
```

### 6) Udělej první tenant a získej API klíč
```bash
curl -sS -X POST http://localhost:8080/api/tenants \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme","contactEmail":"ops@acme.test"}'
```
Z odpovědi si zkopíruj `apiKey`.

### 7) Přidej server
```bash
curl -sS -X POST http://localhost:8080/api/servers \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"name":"Web-01","hostname":"web01.internal","port":443}'
```

### 8) Simuluj výpadek
```bash
curl -sS -X POST http://localhost:8080/api/availability-checks \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"serverNodeId":"<SERVER_ID>","isUp":false,"responseTimeMs":0,"statusCode":503,"errorMessage":"timeout"}'
```

### 9) Zapni SIEM webhook
```bash
curl -sS -X PUT http://localhost:8080/api/integrations/siem \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"webhookUrl":"https://example-siem.local/webhook","sharedSecret":"topsecret","enabled":true}'
```

### 10) Aktivuj placený tarif přes Stripe checkout
```bash
curl -sS -X POST http://localhost:8080/api/billing/checkout \
  -H "X-Overseer-ApiKey: <API_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"planType":1}'
```
Dostaneš `checkoutUrl` → otevři ji v browseru.

---

## Dev helper příkazy
```bash
make backend-build
make frontend-build
make up
make smoke
make down
```

