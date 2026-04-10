export type Dashboard = {
  totalServers: number;
  recentHealthyChecks: number;
  unresolvedSecurityEvents: number;
  licenseRisk: string;
};

export type PricingRow = {
  plan: string;
  priceUsdMonthly: number;
  features: string[];
};

export type TenantBootstrapResult = {
  id: string;
  apiKey: string;
  trialEndsAtUtc: string;
};

export type TenantBootstrapRequest = {
  name: string;
  contactEmail: string;
};
