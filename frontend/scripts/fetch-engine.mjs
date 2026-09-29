#!/usr/bin/env node

/**
 * Fetches the Stockfish builds the analysis panel loads at runtime.
 *
 * This exists instead of an `npm i stockfish` dependency. That package is a 205 MB install
 * that also carries an asm.js build and a CLI wrapper, and it would put the engine under
 * node_modules where every `npm ci` deletes and re-downloads it. Here the files land in
 * `.engine/` once and stay there across installs.
 *
 * Files are pinned by version and verified by SHA-256, so this is not "download whatever is
 * up there today" - a changed byte fails the build rather than shipping quietly.
 *
 * Runs from `postinstall`, and is a no-op once the files are present and verified. It must
 * never hang: npm hides script output behind its spinner, so a stalled download here looks
 * exactly like `npm ci` freezing with no explanation. Every request is therefore abandoned
 * once it stops receiving data, and retried a few times before giving up loudly.
 *
 * Environment:
 *   STOCKFISH_MIRROR  fetch from somewhere else (an internal mirror, an air-gapped build);
 *                     joined with `<version>/bin/<file>` like the default source.
 *   STOCKFISH_SKIP=1  skip entirely - for a host that has the files copied in by hand.
 *   STOCKFISH_TIMEOUT_MS  how long a download may receive nothing before it is retried,
 *                         default 30000.
 *
 * `--optional` downgrades a download failure to a warning. `postinstall` passes it so that a
 * host which cannot reach the mirror can still install: being unable to fetch an engine is an
 * environment problem, not a reason to block every other dependency. `prebuild` runs without
 * it, because a build that quietly omits the engine ships a panel that fails to start - the
 * build output is the deliverable, and it has to be complete or fail. A checksum mismatch is
 * fatal either way: that is corruption or tampering, not a flaky network.
 */

import { createHash } from 'node:crypto';
import { mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const VERSION = '19.0.0';
const DEFAULT_MIRROR = 'https://unpkg.com/stockfish@';

/**
 * Two strengths, each in both threading variants: which variant the page can use depends on
 * whether it was served cross-origin isolated.
 *
 * - Lite (~1.7 MB) is what the panel loads by default. Its network is small enough that
 *   switching the engine on costs about as much as a large image.
 * - Full (~99 MB) carries Stockfish's full-size network. It is noticeably stronger, but only
 *   downloaded by a browser whose user picks it in the engine settings.
 *
 * The package also ships an asm.js build, which predates WebAssembly and is not used here.
 */
const FILES = [
  {
    name: 'stockfish-19-lite.js',
    sha256: '2f98d35d20bf435c16925f8955fe4b0c2062e66962799a407667218ff9ea709d',
    bytes: 32817
  },
  {
    name: 'stockfish-19-lite.wasm',
    sha256: '18727c9ade11a8ca04391ab5a298232bc6fffebe2002e7cfffac82e7ad453447',
    bytes: 1636291
  },
  {
    name: 'stockfish-19-lite-single.js',
    sha256: 'd3344124ab067fb0b90ee77873bb8e9fbf5fc01bc525fe714b0f942581e889e6',
    bytes: 21415
  },
  {
    name: 'stockfish-19-lite-single.wasm',
    sha256: '57ac2d72312aba346760e3f173f687a8c211208e97a87268436f7f0e10bb5387',
    bytes: 1787571
  },
  {
    name: 'stockfish-19.js',
    sha256: '227b9317cb8fc347da722b3f6694c5f57eafe17842a8a1afbfe08c3aeeae5671',
    bytes: 32718
  },
  {
    name: 'stockfish-19.wasm',
    sha256: 'e0ef90031a310479e5b0c3692a9839118ed785535c306252e68ed3300a45b02d',
    bytes: 99065439
  },
  {
    name: 'stockfish-19-single.js',
    sha256: '72772f8bdd7353e4e24245d946bb831f56bcccf02fa16a779c1b92a6c00e5cc2',
    bytes: 21315
  },
  {
    name: 'stockfish-19-single.wasm',
    sha256: '8725c26572762617fd96b2ea83ff130e6640b85815890d682bf8c49db0820721',
    bytes: 99102793
  }
];

const targetDirectory = resolve(dirname(fileURLToPath(import.meta.url)), '..', '.engine');
const mirror = process.env.STOCKFISH_MIRROR ?? DEFAULT_MIRROR;
const timeoutMs = Number(process.env.STOCKFISH_TIMEOUT_MS) || 30_000;
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

/**
 * One attempt, abandoned once the connection goes quiet. The limit is on silence rather than
 * on the whole transfer: the full builds are ~99 MB, which a slow but healthy link needs
 * minutes for, while a dead one sends nothing at all. Without the abort a dead network
 * stalls npm indefinitely.
 */
async function fetchOnce(url) {
  const controller = new AbortController();
  let timer;
  const armStallTimer = () => {
    clearTimeout(timer);
    timer = setTimeout(() => controller.abort(), timeoutMs);
  };

  armStallTimer();

  try {
    const response = await fetch(url, { redirect: 'follow', signal: controller.signal });
    if (!response.ok) {
      throw new Error(`responded ${response.status} ${response.statusText}`);
    }

    const chunks = [];
    for await (const chunk of response.body) {
      chunks.push(chunk);
      armStallTimer();
    }
    return Buffer.concat(chunks);
  } catch (error) {
    if (error.name === 'AbortError' || error.name === 'TimeoutError') {
      throw new Error(`no data for ${Math.round(timeoutMs / 1000)}s`);
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

/**
 * Deletes whatever an earlier version of this script left behind. The build copies engine
 * files by name pattern, so an old build would not ship - but it would sit on the build
 * host's disk indefinitely, which is what this avoids.
 */
async function removeStaleFiles() {
  const wanted = new Set(FILES.map(file => file.name));

  for (const name of await readdir(targetDirectory)) {
    if (!wanted.has(name)) {
      await rm(join(targetDirectory, name), { recursive: true, force: true });
      console.log(`  removed ${name} (not part of Stockfish ${VERSION})`);
    }
  }
}

async function main() {
  if (process.env.STOCKFISH_SKIP === '1') {
    console.log('STOCKFISH_SKIP=1: leaving .engine alone.');
    return;
  }

  await mkdir(targetDirectory, { recursive: true });
  await removeStaleFiles();

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
      'Copied by hand:  put the files listed in scripts/fetch-engine.mjs in frontend/.engine/, then STOCKFISH_SKIP=1 npm run build\n'
  );

  if (tolerable) {
    console.error('\nInstall continues. The build will refuse to run until this is resolved.\n');
    return;
  }

  process.exitCode = 1;
});
