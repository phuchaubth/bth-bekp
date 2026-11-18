/**
 * Base path of the app, set at build time with VITE_BASE_PATH (e.g. "/bekp/" for an IIS sub-application).
 * Always ends with "/".
 */
export const basePath: string = import.meta.env.BASE_URL;

/** Prefixes an absolute app path ("/api/...") with the deployment base path. */
export function appUrl(path: string, base: string = basePath): string {
  const prefix = base.endsWith('/') ? base : `${base}/`;
  return `${prefix}${path.replace(/^\/+/, '')}`;
}

/** React Router basename ("" at the site root). */
export function routerBasename(base: string = basePath): string {
  return base.replace(/\/+$/, '');
}
