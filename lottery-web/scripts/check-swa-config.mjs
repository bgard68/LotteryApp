/**
 * Asserts the Static Web Apps config is present and still carries the security
 * headers, in the BUILT output rather than the source tree.
 *
 * That distinction is the point: `public/staticwebapp.config.json` only reaches
 * Azure if the asset pipeline copies it. A change to angular.json's assets, or
 * a move of the file, would silently ship a site with no CSP and nothing else
 * in the suite would notice - the app would work perfectly.
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

if (failures.length) {
  console.error(`FAIL  ${path}`);
  for (const f of failures) console.error(`      - ${f}`);
  process.exit(1);
}

console.log(`PASS  ${path} - ${Object.keys(headers).length} security headers declared`);
