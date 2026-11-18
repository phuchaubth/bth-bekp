import { appUrl } from '../lib/basePath';

/**
 * Minimal API client. Same-origin requests (Vite proxy in development; in IIS the API also serves the SPA).
 * Access tokens come from a pluggable provider so the hospital identity provider (OIDC) can be wired in later.
 */
export type AccessTokenProvider = () => Promise<string | undefined>;

let tokenProvider: AccessTokenProvider = async () => undefined;

export function setAccessTokenProvider(provider: AccessTokenProvider): void {
  tokenProvider = provider;
}

export function getAccessToken(): Promise<string | undefined> {
  return tokenProvider();
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const token = await tokenProvider();
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(appUrl(path), { headers, signal, credentials: 'same-origin' });
  if (!response.ok) {
    throw new ApiError(response.status, `Yêu cầu thất bại (${response.status})`);
  }

  return (await response.json()) as T;
}
