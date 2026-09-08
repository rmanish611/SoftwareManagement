import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * How each route is produced on the server.
 *
 * Anything that reads content renders per request (`RenderMode.Server`), not at build time.
 * Prerendering would freeze whatever the API happened to return during the build, and worse, the
 * build machine has no API to call at all, so the prerender simply fails. Rendering per request
 * also means a page published at 09:00 is live at 09:00 rather than at the next deployment.
 *
 * The admin area is rendered in the browser: it is behind a sign-in, so there is nothing for a
 * crawler to index and no reason to pay for server rendering.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'admin', renderMode: RenderMode.Client },
  { path: 'admin/**', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Server },
];
