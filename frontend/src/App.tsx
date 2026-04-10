import { useEffect, useMemo, useState } from 'react';
import { apiClient } from './api/client';
import type { Dashboard, PricingRow } from './types/models';

export function App(): JSX.Element {
  const [pricing, setPricing] = useState<PricingRow[]>([]);
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [apiKey, setApiKey] = useState<string>(() => localStorage.getItem('overseer_api_key') ?? '');
  const [tenantName, setTenantName] = useState<string>('Acme Operations');
  const [tenantEmail, setTenantEmail] = useState<string>('ops@acme.test');
  const [info, setInfo] = useState<string>('');
  const [error, setError] = useState<string>('');
  const [isLoadingDashboard, setIsLoadingDashboard] = useState(false);

  useEffect(() => {
    apiClient.pricing().then(setPricing).catch(() => setError('Nepodařilo se načíst ceník.'));
  }, []);

  const loadDashboard = async (overrideKey?: string): Promise<void> => {
    setError('');
    setIsLoadingDashboard(true);

    try {
      const resolvedKey = overrideKey ?? apiKey;
      const data = await apiClient.dashboard(resolvedKey);
      setDashboard(data);
    } catch {
      setError('Dashboard se nepodařilo načíst. Ověř API key a dostupnost backendu.');
    } finally {
      setIsLoadingDashboard(false);
    }
  };

  useEffect(() => {
    if (!apiKey) {
      return;
    }

    void loadDashboard(apiKey);
  }, [apiKey]);

  const cards = useMemo(() => {
    if (!dashboard) {
      return [
        { label: 'Monitorované servery', value: '—' },
        { label: 'Poslední zdravé kontroly', value: '—' },
        { label: 'Nevyřešené bezpečnostní události', value: '—' },
        { label: 'Riziko licence', value: '—' }
      ];
    }

    return [
      { label: 'Monitorované servery', value: dashboard.totalServers },
      { label: 'Poslední zdravé kontroly', value: dashboard.recentHealthyChecks },
      { label: 'Nevyřešené bezpečnostní události', value: dashboard.unresolvedSecurityEvents },
      { label: 'Riziko licence', value: dashboard.licenseRisk }
    ];
  }, [dashboard]);

  const saveApiKey = (value: string): void => {
    setApiKey(value);
    localStorage.setItem('overseer_api_key', value);
  };

  const handleCreateTenant = async (): Promise<void> => {
    setError('');
    setInfo('');

    try {
      const result = await apiClient.createTenant({ name: tenantName, contactEmail: tenantEmail });
      saveApiKey(result.apiKey);
      setInfo(`Tenant vytvořen. Trial běží do ${new Date(result.trialEndsAtUtc).toLocaleString()}. API key byl uložen do localStorage.`);
      await loadDashboard(result.apiKey);
    } catch {
      setError('Tenant se nepodařilo vytvořit. Zkontroluj backend (http://localhost:8080/swagger).');
    }
  };

  const handleCheckout = async (plan: string): Promise<void> => {
    setError('');
    setInfo('');
    const normalized = plan.toLowerCase();
    const planType = normalized === 'trial' ? 0 : normalized === 'subscription' ? 1 : 2;

    try {
      const result = await apiClient.checkout(planType, apiKey);
      window.open(result.checkoutUrl, '_blank', 'noopener,noreferrer');
      setInfo(`Objednávka ${result.checkoutReference} vytvořena (${result.amountUsd} USD/měsíc). Stripe checkout byl otevřen v novém panelu.`);
    } catch {
      setError('Checkout selhal. Nejprve vlož platný API key nebo vytvoř tenant.');
    }
  };

  return (
    <main className="layout">
      <section className="hero surface">
        <p className="eyebrow">SERVER RELIABILITY + SECURITY + LICENSING</p>
        <h1>Overseer Control Center</h1>
        <p>Monitoring dostupnosti serverů, bezpečnostních incidentů a licenční compliance v jednom přehledném dashboardu.</p>
      </section>

      <section className="cards">
        {cards.map((card) => (
          <article key={card.label} className="card">
            <h3>{card.label}</h3>
            <p>{card.value}</p>
          </article>
        ))}
      </section>

      <section className="surface actions">
        <div>
          <h2>Rychlý start (bez env nastavování)</h2>
          <p>1) Vytvoř tenant, 2) API key se uloží lokálně, 3) načti dashboard.</p>
          <div className="inline-form">
            <input value={tenantName} onChange={(event) => setTenantName(event.target.value)} placeholder="Název firmy" />
            <input value={tenantEmail} onChange={(event) => setTenantEmail(event.target.value)} placeholder="Kontaktní e-mail" />
            <button type="button" onClick={() => void handleCreateTenant()}>Vytvořit tenant</button>
          </div>
        </div>
        <div>
          <h3>API key</h3>
          <div className="inline-form">
            <input value={apiKey} onChange={(event) => saveApiKey(event.target.value)} placeholder="X-Overseer-ApiKey" />
            <button type="button" onClick={() => void loadDashboard()} disabled={isLoadingDashboard}>
              {isLoadingDashboard ? 'Načítám…' : 'Načíst dashboard'}
            </button>
          </div>
        </div>
      </section>

      <section>
        <h2>Tarify a monetizace</h2>
        <div className="pricing-grid">
          {pricing.map((plan) => (
            <article key={plan.plan} className="pricing-card">
              <h3>{plan.plan}</h3>
              <p className="price">${plan.priceUsdMonthly}<span>/měsíc</span></p>
              <ul>
                {plan.features.map((feature) => <li key={feature}>{feature}</li>)}
              </ul>
              <button type="button" onClick={() => handleCheckout(plan.plan)}>
                Aktivovat {plan.plan}
              </button>
            </article>
          ))}
        </div>
      </section>

      <section className="nice-to-have">
        <h2>Nice-to-have funkcionality v základu</h2>
        <ul>
          <li>SLA reporting a trendy latence.</li>
          <li>Audit log pro všechny změny alertů a licenčních záznamů.</li>
          <li>Bezpečnostní webhook endpointy pro SIEM integrace.</li>
          <li>Konfigurovatelné SSO/SAML v Enterprise tarifu.</li>
        </ul>
      </section>

      {error && <p className="error">{error}</p>}
      {info && <p className="message">{info}</p>}
    </main>
  );
}
