import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();

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
