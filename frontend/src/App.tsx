import { useEffect, useMemo, useState } from 'react';
import { apiClient } from './api/client';
import type { Dashboard, PricingRow } from './types/models';

export function App(): JSX.Element {
  const [pricing, setPricing] = useState<PricingRow[]>([]);
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string>('');
  const [checkoutMessage, setCheckoutMessage] = useState<string>('');

  useEffect(() => {
    apiClient.pricing().then(setPricing).catch(() => setError('Nepodařilo se načíst ceník.'));
    apiClient.dashboard().then(setDashboard).catch(() => setError('Pro dashboard nastavte VITE_API_KEY.'));
  }, []);

  const cards = useMemo(() => {
    if (!dashboard) {
      return [];
    }

    return [
      { label: 'Monitorované servery', value: dashboard.totalServers },
      { label: 'Poslední zdravé kontroly', value: dashboard.recentHealthyChecks },
      { label: 'Nevyřešené bezpečnostní události', value: dashboard.unresolvedSecurityEvents },
      { label: 'Riziko licence', value: dashboard.licenseRisk }
    ];
  }, [dashboard]);

  const handleCheckout = async (plan: string): Promise<void> => {
    setCheckoutMessage('');
    const normalized = plan.toLowerCase();
    const planType = normalized === 'trial' ? 0 : normalized === 'subscription' ? 1 : 2;

    try {
      const result = await apiClient.checkout(planType);
      setCheckoutMessage(`Objednávka vytvořena (${result.checkoutReference}), částka $${result.amountUsd}/měsíc.`);
    } catch {
      setCheckoutMessage('Checkout selhal. Ověřte API klíč nebo backend.');
    }
  };

  return (
    <main className="layout">
      <section className="hero">
        <h1>Overseer</h1>
        <p>Moderní monitoring dostupnosti serverů, bezpečnostních incidentů a compliance licencí Windows Serveru.</p>
      </section>

      <section className="cards">
        {cards.map((card) => (
          <article key={card.label} className="card">
            <h3>{card.label}</h3>
            <p>{card.value}</p>
          </article>
        ))}
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
          <li>Připraveno pro SSO/SAML v Enterprise tarifu.</li>
        </ul>
      </section>

      {error && <p className="error">{error}</p>}
      {checkoutMessage && <p className="message">{checkoutMessage}</p>}
    </main>
  );
}
