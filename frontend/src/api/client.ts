import type { Dashboard, PricingRow, TenantBootstrapRequest, TenantBootstrapResult } from '../types/models';

const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080';
const defaultApiKey = import.meta.env.VITE_API_KEY ?? '';

async function request<T>(path: string, init?: RequestInit, apiKey?: string): Promise<T> {
  const resolvedApiKey = apiKey ?? defaultApiKey;
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(resolvedApiKey ? { 'X-Overseer-ApiKey': resolvedApiKey } : {}),
      ...(init?.headers ?? {})
    }
  });

  if (!response.ok) {
    throw new Error(`Request failed (${response.status})`);
  }

  return response.json() as Promise<T>;
}

export const apiClient = {
  pricing: () => request<PricingRow[]>('/api/public/pricing'),
  createTenant: (payload: TenantBootstrapRequest) => request<TenantBootstrapResult>('/api/tenants', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  dashboard: (apiKey?: string) => request<Dashboard>('/api/dashboard', undefined, apiKey),
  checkout: (planType: number, apiKey?: string) => request<{ checkoutReference: string; amountUsd: number; checkoutUrl: string }>('/api/billing/checkout', {
    method: 'POST',
    body: JSON.stringify({ planType })
  }, apiKey)
};
