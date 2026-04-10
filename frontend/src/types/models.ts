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
