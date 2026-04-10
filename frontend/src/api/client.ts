import type { Dashboard, PricingRow } from '../types/models';

const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080';
const apiKey = import.meta.env.VITE_API_KEY ?? '';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(apiKey ? { 'X-Overseer-ApiKey': apiKey } : {}),
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
  dashboard: () => request<Dashboard>('/api/dashboard'),
  checkout: (planType: number) => request<{ checkoutReference: string; amountUsd: number }>('/api/billing/checkout', {
    method: 'POST',
    body: JSON.stringify({ planType })
  })
};
