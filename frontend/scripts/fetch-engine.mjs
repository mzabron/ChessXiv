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
 * Runs from `postinstall`, and is a no-op once the files are present and verified.
 * Set STOCKFISH_MIRROR to fetch from somewhere else (an internal mirror, an air-gapped
 * build); it is joined with `<version>/bin/<file>` the same way the default source is.
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

async function download(file) {
  const url = `${mirror}${VERSION}/bin/${file.name}`;
  const response = await fetch(url, { redirect: 'follow' });

  if (!response.ok) {
    throw new Error(`${url} responded ${response.status} ${response.statusText}`);
  }

  const body = Buffer.from(await response.arrayBuffer());
  const digest = digestOf(body);

  if (digest !== file.sha256) {
    throw new Error(
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
  console.error('\nCould not fetch the Stockfish engine files.\n');
  console.error(error.message);
  console.error(
    '\nThe app builds without them, but the analysis panel will fail to start.\n' +
      `Retry with: npm run engine:fetch  (source: ${mirror}${VERSION}/bin/)\n`
  );
  process.exitCode = 1;
});
