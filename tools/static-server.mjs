// Dependency-free static file server used by the phase gate to serve the production Angular
// build exactly as a real host would.
//
// The single-page fallback is a genuine fallback, not a rubber stamp: a request for a path
// that carries a file extension returns 404. Without that rule every request would answer 200,
// and the deep-route check in the gate would prove nothing at all.
//
// Usage: node tools/static-server.mjs <dist-directory> [port]
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { join, extname, normalize, resolve } from 'node:path';

const root = resolve(normalize(process.argv[2] ?? ''));
const port = Number(process.argv[3] || 4300);

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
  '.xml': 'application/xml; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
};

createServer(async (req, res) => {
  try {
    const url = decodeURIComponent((req.url ?? '/').split('?')[0]);
    let filePath = resolve(normalize(join(root, url === '/' ? '/index.html' : url)));

    // Never serve anything outside the published directory.
    if (!filePath.startsWith(root)) {
      res.writeHead(403);
      res.end('forbidden');
      return;
    }

    let info = await stat(filePath).catch(() => null);

    if (!info || info.isDirectory()) {
      const extension = extname(filePath);
      if (extension && extension !== '.html') {
        res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
        res.end('not found');
        return;
      }

      filePath = join(root, 'index.html');
      info = await stat(filePath);
    }

    const body = await readFile(filePath);
    res.writeHead(200, {
      'content-type': types[extname(filePath)] ?? 'application/octet-stream',
      'content-length': body.length,
      'cache-control': extname(filePath) === '.html' ? 'no-cache' : 'public, max-age=31536000, immutable',
    });
    res.end(body);
  } catch (error) {
    res.writeHead(500, { 'content-type': 'text/plain; charset=utf-8' });
    res.end(String(error));
  }
}).listen(port, () => console.log(`static-server ${port} root=${root}`));
