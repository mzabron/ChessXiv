#!/usr/bin/env node

/**
 * Fetches the Stockfish builds the analysis panel loads at runtime.
 *
 * This exists instead of an `npm i stockfish` dependency. That package ships every build it
 * has, including two 113 MB full-strength ones the browser cannot sensibly download: 167 MB
 * over the wire and 248 MB on disk, of which we use 14 MB. On a machine that builds and
 * serves the app, that is 234 MB of nothing.
 *
 * Files are pinned by version and verified by SHA-256, so this is not "download whatever is
 * up there today" - a changed byte fails the build rather than shipping quietly.
 *
 * Runs from `postinstall`, and is a no-op once the files are present and verified. It must
 * never hang: npm hides script output behind its spinner, so a stalled download here looks
 * exactly like `npm ci` freezing with no explanation. Every request is therefore bounded by
 * a timeout and retried a few times before giving up loudly.
 *
 * Environment:
 *   STOCKFISH_MIRROR  fetch from somewhere else (an internal mirror, an air-gapped build);
 *                     joined with `<version>/bin/<file>` like the default source.
 *   STOCKFISH_SKIP=1  skip entirely - for a host that has the files copied in by hand.
 *   STOCKFISH_TIMEOUT_MS  per-attempt timeout, default 60000.
 *
 * `--optional` downgrades a download failure to a warning. `postinstall` passes it so that a
 * host which cannot reach the mirror can still install: being unable to fetch an engine is an
 * environment problem, not a reason to block every other dependency. `prebuild` runs without
 * it, because a build that quietly omits the engine ships a panel that fails to start - the
 * build output is the deliverable, and it has to be complete or fail. A checksum mismatch is
 * fatal either way: that is corruption or tampering, not a flaky network.
 */

import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const VERSION = '18.0.8';
const DEFAULT_MIRROR = 'https://unpkg.com/stockfish@';

/**
 * Only the "lite" builds. The full ones are ~113 MB each: stronger, but nobody is
 * downloading that in a browser tab. Both threading variants ship because which one the
 * page can use depends on whether it was served cross-origin isolated.
 */
const FILES = [
  {
    name: 'stockfish-18-lite.js',
    sha256: '6e64f417a642c2f2a27d33c09f069522366d1bc33ed7ee8712afcc347e109af4',
    bytes: 32868
  },
  {
    name: 'stockfish-18-lite.wasm',
    sha256: 'd50136919dcd90e75eb8df78b255d47d618962b670028b38961343f6eb409174',
    bytes: 7093151
  },
  {
    name: 'stockfish-18-lite-single.js',
    sha256: '5243fd9b276cab7dfe3ad1d43ab9ead73568fac76468c614242977a210c4a391',
    bytes: 21429
  },
  {
    name: 'stockfish-18-lite-single.wasm',
    sha256: 'a8fbc05ec6920b56d7485826dcb02c5ffd2826bcbf751cf973046f237a9096f1',
    bytes: 7295411
  }
];

const targetDirectory = resolve(dirname(fileURLToPath(import.meta.url)), '..', '.engine');
const mirror = process.env.STOCKFISH_MIRROR ?? DEFAULT_MIRROR;
const timeoutMs = Number(process.env.STOCKFISH_TIMEOUT_MS) || 60_000;
const ATTEMPTS = 3;
const isOptional = process.argv.includes('--optional');

/** Marks the one failure that is never tolerable, however the script was invoked. */
class ChecksumError extends Error {}

function digestOf(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

async function alreadyPresent(file) {
  try {
    const existing = await readFile(join(targetDirectory, file.name));
    return digestOf(existing) === file.sha256;
  } catch {
    return false;
  }
}

/** One attempt, bounded in time. Without the abort a dead network stalls npm indefinitely. */
async function fetchOnce(url) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, { redirect: 'follow', signal: controller.signal });
    if (!response.ok) {
      throw new Error(`responded ${response.status} ${response.statusText}`);
    }
    return Buffer.from(await response.arrayBuffer());
  } catch (error) {
    if (error.name === 'AbortError' || error.name === 'TimeoutError') {
      throw new Error(`no response within ${Math.round(timeoutMs / 1000)}s`);
    }
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

async function download(file) {
  const url = `${mirror}${VERSION}/bin/${file.name}`;

  let body;
  for (let attempt = 1; attempt <= ATTEMPTS; attempt++) {
    try {
      body = await fetchOnce(url);
      break;
    } catch (error) {
      if (attempt === ATTEMPTS) {
        throw new Error(`${url}\n  ${error.message} (after ${ATTEMPTS} attempts)`);
      }
      console.log(`  ${file.name}: ${error.message}, retrying (${attempt + 1}/${ATTEMPTS})…`);
      await new Promise(resolve => setTimeout(resolve, 2000 * attempt));
    }
  }

  const digest = digestOf(body);

  if (digest !== file.sha256) {
    throw new ChecksumError(
      `${file.name} does not match its pinned checksum.\n` +
        `  expected ${file.sha256}\n  received ${digest}\n` +
        `  from     ${url}\n` +
        'Refusing to write it. If the upstream build legitimately changed, update the ' +
        'checksums in this script deliberately.'
    );
  }

  await writeFile(join(targetDirectory, file.name), body);
  return body.length;
}

async function main() {
  if (process.env.STOCKFISH_SKIP === '1') {
    console.log('STOCKFISH_SKIP=1: leaving .engine alone.');
    return;
  }

  await mkdir(targetDirectory, { recursive: true });

  const wanted = [];
  for (const file of FILES) {
    if (await alreadyPresent(file)) {
      continue;
    }
    wanted.push(file);
  }

  if (wanted.length === 0) {
    console.log(`Stockfish ${VERSION}: already present in .engine, nothing to fetch.`);
    return;
  }

  const total = wanted.reduce((sum, file) => sum + file.bytes, 0);
  console.log(`Fetching Stockfish ${VERSION} (${wanted.length} files, ~${Math.round(total / 1e6)} MB)…`);

  for (const file of wanted) {
    const bytes = await download(file);
    console.log(`  ${file.name} (${Math.round(bytes / 1e3)} kB)`);
  }
}

main().catch(error => {
  const tolerable = isOptional && !(error instanceof ChecksumError);

  console.error(`\n${tolerable ? 'Warning: could not' : 'Could not'} fetch the Stockfish engine files.\n`);
  console.error(error.message);
  console.error(
    '\n' +
      `Retry with:      npm run engine:fetch      (source: ${mirror}${VERSION}/bin/)\n` +
      'Behind a proxy:  STOCKFISH_MIRROR=<base-url> npm run engine:fetch\n' +
      'Copied by hand:  put the four files in frontend/.engine/, then STOCKFISH_SKIP=1 npm run build\n'
  );

  if (tolerable) {
    console.error('\nInstall continues. The build will refuse to run until this is resolved.\n');
    return;
  }

  process.exitCode = 1;
});
