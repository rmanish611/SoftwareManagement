import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';
import { Readable } from 'node:stream';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();

/**
 * Where the API lives, for requests this process makes on a visitor's behalf.
 *
 * Without this the site renders empty. A component asks for `/api/v1/public/products`; in a browser
 * that resolves against the site's own origin and a reverse proxy forwards it, but during
 * server-side rendering it resolves against this very server, which answers with a rendered HTML
 * page. The component gets HTML where it expected JSON, treats it as a failure, and renders its
 * empty state. The page is then served to a search engine with no products on it, which defeats the
 * entire reason for rendering on the server (NFR-SEO-01).
 *
 * So this process forwards `/api` to the API itself. The same forwarding serves the browser, which
 * means one origin, no cross-origin configuration and no second hostname to keep in step.
 */
const apiBaseUrl = (process.env['API_BASE_URL'] ?? '').replace(/\/$/, '');

/** Headers that describe one connection and must not be copied onto another one. */
const hopByHopHeaders = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
  'host',
  'content-length',
]);

if (apiBaseUrl) {
  app.use('/api', (req, res) => {
    const target = apiBaseUrl + '/api' + req.url;

    const headers = new Headers();
    for (const [name, value] of Object.entries(req.headers)) {
      if (value === undefined || hopByHopHeaders.has(name.toLowerCase())) {
        continue;
      }

      headers.set(name, Array.isArray(value) ? value.join(', ') : value);
    }

    // The original host is passed on separately so the API can build absolute URLs that point at
    // the site rather than at itself.
    if (req.headers.host) {
      headers.set('x-forwarded-host', req.headers.host);
    }

    const hasBody = req.method !== 'GET' && req.method !== 'HEAD';

    fetch(target, {
      method: req.method,
      headers,
      // The body is streamed rather than buffered, so a large upload does not sit in this
      // process's memory on its way through.
      body: hasBody ? (Readable.toWeb(req) as ReadableStream) : undefined,
      duplex: hasBody ? 'half' : undefined,
      redirect: 'manual',
    } as RequestInit)
      .then(async (upstream) => {
        res.status(upstream.status);

        upstream.headers.forEach((value, name) => {
          if (!hopByHopHeaders.has(name.toLowerCase())) {
            res.setHeader(name, value);
          }
        });

        const buffer = Buffer.from(await upstream.arrayBuffer());
        res.end(buffer);
      })
      .catch(() => {
        // The API being unreachable is a 502 from this server: it is not this process's fault, and
        // it must not be reported as a rendering failure.
        res.status(502).type('application/json').send('{"title":"The API is not reachable."}');
      });
  });
}

/**
 * Angular refuses to render a request whose Host header it does not recognise, which is what stops
 * a server-side request-forgery attack from making the renderer fetch a URL of the attacker's
 * choosing. That means every hostname the site is actually served on has to be listed.
 *
 * The list comes from configuration so a deployment does not need a rebuild: set
 * `ALLOWED_HOSTS=softwaremanagement.example,www.softwaremanagement.example`. The localhost entries
 * are the ones the phase gate and a developer use.
 */
const allowedHosts = (process.env['ALLOWED_HOSTS'] ?? '')
  .split(',')
  .map((host) => host.trim())
  .filter(Boolean)
  .concat(['localhost', '127.0.0.1', 'localhost:4300', 'localhost:4000', 'localhost:4399']);

const angularApp = new AngularNodeAppEngine({ allowedHosts });

/**
 * Serve the built files. Hashed assets are immutable, so a long cache is safe; index.html is not
 * served from here, because every route is rendered.
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * A request for a file that does not exist is a 404, not a rendered page.
 *
 * Without this, a missing `main-abc123.js` answers 200 with HTML, the browser tries to execute
 * that HTML as JavaScript, and the real fault, a bad asset reference, is hidden behind a page that
 * looks like it loaded. Anything with a file extension other than `.html` is a static asset: if
 * express.static did not serve it, it is not there.
 */
const staticAssetPattern = /\.[a-z0-9]+$/i;

app.use((req, res, next) => {
  const path = req.path;
  if (staticAssetPattern.test(path) && !path.endsWith('.html')) {
    res.status(404).type('text/plain').send('not found');
    return;
  }

  next();
});

/** Render everything else with Angular. */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/** Request handler used by the Angular CLI during development and by serverless hosts. */
export const reqHandler = createNodeRequestHandler(app);
