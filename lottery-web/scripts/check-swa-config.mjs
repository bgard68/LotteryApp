/**
 * Asserts the Static Web Apps config is present and still carries the security
 * headers, in the BUILT output rather than the source tree.
 *
 * That distinction is the point: `public/staticwebapp.config.json` only reaches
 * Azure if the asset pipeline copies it. A change to angular.json's assets, or
 * a move of the file, would silently ship a site with no CSP and nothing else
 * in the suite would notice - the app would work perfectly.
 *
 * It also scans the built index.html for inline event handlers. The CSP says
 * script-src 'self', and Angular's inlineCritical optimization once emitted
 * <link ... onload="..."> - blocked by the browser, so the full stylesheet
 * silently stayed media="print". The build config disables that optimization;
 * this check is what fails if it, or anything like it, comes back.
 *
 * Usage: node scripts/check-swa-config.mjs [dir]   (default: the build output)
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const dir = process.argv[2] ?? 'dist/lottery-web/browser';
const path = join(dir, 'staticwebapp.config.json');

const REQUIRED_HEADERS = {
  'Content-Security-Policy': ["frame-ancestors 'none'", "object-src 'none'", "default-src 'self'"],
  'X-Frame-Options': ['DENY'],
  'X-Content-Type-Options': ['nosniff'],
  'Referrer-Policy': ['no-referrer'],
  'Permissions-Policy': ['camera=()'],
};

const failures = [];

let config;
try {
  config = JSON.parse(readFileSync(path, 'utf8'));
} catch (err) {
  console.error(`FAIL  ${path} is missing or unparseable - the deployed site would have no security headers.`);
  console.error(`      ${err.message}`);
  process.exit(1);
}

const headers = config.globalHeaders ?? {};
for (const [header, fragments] of Object.entries(REQUIRED_HEADERS)) {
  const value = headers[header];
  if (!value) {
    failures.push(`${header} is not declared`);
    continue;
  }
  for (const fragment of fragments) {
    if (!value.includes(fragment)) failures.push(`${header} no longer contains "${fragment}"`);
  }
}

// The API is a different origin (the free SWA SKU has no linked backend), so a
// CSP that forgets connect-src breaks every request the app makes.
const csp = headers['Content-Security-Policy'] ?? '';
if (!csp.includes('connect-src')) {
  failures.push('Content-Security-Policy has no connect-src - the app could not call its own API');
}

// The CSP forbids inline script, so shipped HTML must not contain any - an
// inline handler is not "less secure", it is DEAD: the browser blocks it and
// whatever it was meant to do silently never happens.
const indexPath = join(dir, 'index.html');
try {
  const html = readFileSync(indexPath, 'utf8');
  const handlers = html.match(/\son[a-z]+\s*=\s*["'][^"']*["']/gi) ?? [];
  for (const h of handlers) failures.push(`index.html ships an inline handler the CSP will block: ${h.trim().slice(0, 60)}`);
  if (/<script(?![^>]*\ssrc=)[^>]*>[^<]/i.test(html)) {
    failures.push('index.html ships an inline <script> body the CSP will block');
  }
} catch (err) {
  failures.push(`index.html missing or unreadable: ${err.message}`);
}

if (failures.length) {
  console.error(`FAIL  ${dir}`);
  for (const f of failures) console.error(`      - ${f}`);
  process.exit(1);
}

console.log(`PASS  ${dir} - ${Object.keys(headers).length} security headers declared, index.html free of inline script`);
