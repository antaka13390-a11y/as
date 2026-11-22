import crypto from 'crypto';
import dotenv from 'dotenv';
import express from 'express';
import type { NextFunction, Request, Response } from 'express';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

dotenv.config();

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ROOT_DIR = __dirname;
const DATA_DIR = path.join(ROOT_DIR, 'data');
const STORE_PATH = path.join(DATA_DIR, 'cyberhub-store.json');
const DOTNET_ROOT = path.resolve(ROOT_DIR, 'dotnet');

type LaunchType = 'DirectExecutable' | 'Steam' | 'RiotClient' | 'ExternalLauncher';

interface StorageLocation {
  id: string;
  displayName: string;
  uncRootPath: string;
  description: string;
  isReadOnlyForClients: boolean;
}

interface GameEntry {
  id: string;
  name: string;
  category: string;
  description: string;
  developer: string;
  coverImagePath: string;
  heroImagePath: string;
  storageLocationId: string;
  executablePath: string;
  launchArguments: string;
  launchType: LaunchType;
  externalAppId: string;
  processNameToMonitor: string;
  isVisible: boolean;
  isFeatured: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface AdminAuditLog {
  id: number;
  timestampUtc: string;
  action: string;
  targetGameId: string;
  targetGameName: string;
  workstationName: string;
  sourceIp: string;
  details: string;
}

interface StoreData {
  catalogVersion: number;
  lastBackupUtc: string;
  adminPasswordHash: string;
  storageLocations: StorageLocation[];
  games: GameEntry[];
  auditLogs: AdminAuditLog[];
}

const SESSION_SECRET = process.env.SESSION_SECRET && process.env.SESSION_SECRET.length >= 32
  ? Buffer.from(process.env.SESSION_SECRET, 'utf8')
  : crypto.randomBytes(32);

function hashPassword(password: string): string {
  const salt = crypto.randomBytes(16);
  const derived = crypto.scryptSync(password, salt, 64);
  return `scrypt:${salt.toString('hex')}:${derived.toString('hex')}`;
}

function verifyPassword(password: string, encoded: string): boolean {
  const parts = encoded.split(':');
  if (parts.length !== 3 || parts[0] !== 'scrypt') {
    return false;
  }
  try {
    const salt = Buffer.from(parts[1], 'hex');
    const expected = Buffer.from(parts[2], 'hex');
    const actual = crypto.scryptSync(password, salt, expected.length);
    return crypto.timingSafeEqual(actual, expected);
  } catch {
    return false;
  }
}

function verifyEnvPasscode(input: string, envPasscode: string): boolean {
  const a = Buffer.from(input, 'utf8');
  const b = Buffer.from(envPasscode, 'utf8');
  if (a.length !== b.length) {
    return false;
  }
  return crypto.timingSafeEqual(a, b);
}

function issueToken(workstation: string): { accessToken: string; expiresAtUtc: string } {
  const expMs = Date.now() + 8 * 60 * 60 * 1000;
  const payload = Buffer.from(JSON.stringify({ sub: 'CyberHubAdmin', workstation, expMs }), 'utf8').toString('base64url');
  const sig = crypto.createHmac('sha256', SESSION_SECRET).update(payload).digest('base64url');
  return {
    accessToken: `${payload}.${sig}`,
    expiresAtUtc: new Date(expMs).toISOString(),
  };
}

function verifyToken(token: string): { workstation: string } | null {
  const parts = token.split('.');
  if (parts.length !== 2) return null;
  const [payloadB64, sigB64] = parts;
  const expectedSig = crypto.createHmac('sha256', SESSION_SECRET).update(payloadB64).digest('base64url');
  const a = Buffer.from(sigB64, 'utf8');
  const b = Buffer.from(expectedSig, 'utf8');
  if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) return null;
  try {
    const parsed = JSON.parse(Buffer.from(payloadB64, 'base64url').toString('utf8')) as {
      workstation?: string;
      expMs?: number;
    };
    if (!parsed.expMs || Date.now() >= parsed.expMs) return null;
    return { workstation: parsed.workstation || 'ADMIN-PC' };
  } catch {
    return null;
  }
}

function createInitialStore(): StoreData {
  const now = new Date().toISOString();
  return {
    catalogVersion: 1,
    lastBackupUtc: '',
    adminPasswordHash: '',
    storageLocations: [
      {
        id: 'ccboot-primary',
        displayName: 'CCBoot Game Disk (Primary)',
        uncRootPath: '\\\\CCBOOT-SRV\\Games\\CCBoot',
        description: 'Primary CCBoot diskless iSCSI game image share',
        isReadOnlyForClients: true,
      },
      {
        id: 'smb-steam',
        displayName: 'Server Steam Library Share',
        uncRootPath: '\\\\CCBOOT-SRV\\Games\\SteamLibrary\\steamapps\\common',
        description: 'Windows Server 2022 SMB read-only Steam game vault',
        isReadOnlyForClients: true,
      },
      {
        id: 'smb-riot',
        displayName: 'Riot & Competitive Share',
        uncRootPath: '\\\\CCBOOT-SRV\\Games\\Riot Games',
        description: 'Shared competitive esports game directory',
        isReadOnlyForClients: true,
      },
    ],
    games: [
      {
        id: '11111111-1111-4444-8888-100000000001',
        name: 'Valor Protocol: Zero',
        category: 'Tactical FPS',
        description: '5v5 precision tactical arena shooter configured for 240Hz station displays with low-latency network profiles and automatic anti-cheat handoff.',
        developer: 'Vanguard Interactive',
        coverImagePath: '/src/assets/images/cover_valor_protocol_1795374591215.jpg',
        heroImagePath: '/src/assets/images/hero_cyber_arena_1795374591030.jpg',
        storageLocationId: 'smb-riot',
        executablePath: '\\\\CCBOOT-SRV\\Games\\Riot Games\\Riot Client\\RiotClientServices.exe',
        launchArguments: '--headless',
        launchType: 'RiotClient',
        externalAppId: 'valorant',
        processNameToMonitor: 'VALORANT-Win64-Shipping',
        isVisible: true,
        isFeatured: true,
        sortOrder: 1,
        createdAtUtc: now,
        updatedAtUtc: now,
      },
      {
        id: '11111111-1111-4444-8888-100000000002',
        name: 'Rift Vanguard',
        category: 'MOBA',
        description: 'Competitive 5v5 lane strategy tournament build deployed across CCBoot shared storage with tournament client flags.',
        developer: 'Obsidian Forge',
        coverImagePath: '/src/assets/images/cover_rift_vanguard_1795374591582.jpg',
        heroImagePath: '/src/assets/images/hero_cyber_arena_1795374591030.jpg',
        storageLocationId: 'smb-steam',
        executablePath: '\\\\CCBOOT-SRV\\Games\\SteamLibrary\\steamapps\\common\\RiftVanguard\\vanguard.exe',
        launchArguments: '-novid -high',
        launchType: 'Steam',
        externalAppId: '570',
        processNameToMonitor: 'vanguard',
        isVisible: true,
        isFeatured: false,
        sortOrder: 2,
        createdAtUtc: now,
        updatedAtUtc: now,
      },
      {
        id: '11111111-1111-4444-8888-100000000003',
        name: 'Eclipse Chronicles',
        category: 'Action RPG',
        description: 'Dark gothic action RPG stored on the primary CCBoot diskless game volume for instant zero-copy station streaming.',
        developer: 'Nocturne Studio',
        coverImagePath: '/src/assets/images/cover_eclipse_chronicles_1795374591361.jpg',
        heroImagePath: '/src/assets/images/hero_cyber_arena_1795374591030.jpg',
        storageLocationId: 'ccboot-primary',
        executablePath: '\\\\CCBOOT-SRV\\Games\\CCBoot\\EclipseChronicles\\Eclipse.exe',
        launchArguments: '-dx12 -fullscreen',
        launchType: 'DirectExecutable',
        externalAppId: '',
        processNameToMonitor: 'Eclipse',
        isVisible: true,
        isFeatured: false,
        sortOrder: 3,
        createdAtUtc: now,
        updatedAtUtc: now,
      },
      {
        id: '11111111-1111-4444-8888-100000000004',
        name: 'Apex Overdrive',
        category: 'Racing',
        description: 'High-speed midnight street racing simulator pre-calibrated for station steering wheels and ultra-wide frame pacing.',
        developer: 'Velocity Works',
        coverImagePath: '/src/assets/images/cover_apex_overdrive_1795374590869.jpg',
        heroImagePath: '/src/assets/images/hero_cyber_arena_1795374591030.jpg',
        storageLocationId: 'ccboot-primary',
        executablePath: '\\\\CCBOOT-SRV\\Games\\CCBoot\\ApexOverdrive\\Overdrive.exe',
        launchArguments: '-borderless',
        launchType: 'DirectExecutable',
        externalAppId: '',
        processNameToMonitor: 'Overdrive',
        isVisible: true,
        isFeatured: false,
        sortOrder: 4,
        createdAtUtc: now,
        updatedAtUtc: now,
      },
    ],
    auditLogs: [
      {
        id: 1,
        timestampUtc: now,
        action: 'CATALOG_INITIALIZED',
        targetGameId: '',
        targetGameName: '',
        workstationName: 'CCBOOT-SRV',
        sourceIp: '127.0.0.1',
        details: 'Initialized central game library with CCBoot and SMB storage definitions.',
      },
    ],
  };
}

function loadStore(): StoreData {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  if (!fs.existsSync(STORE_PATH)) {
    const initial = createInitialStore();
    saveStore(initial);
    return initial;
  }
  try {
    return JSON.parse(fs.readFileSync(STORE_PATH, 'utf8')) as StoreData;
  } catch {
    const initial = createInitialStore();
    saveStore(initial);
    return initial;
  }
}

function saveStore(store: StoreData): void {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = `${STORE_PATH}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(store, null, 2), 'utf8');
  fs.renameSync(tmp, STORE_PATH);
}

function validateGameInput(body: Record<string, unknown>): { valid: boolean; error?: string } {
  const name = typeof body.name === 'string' ? body.name.trim() : '';
  const launchType = typeof body.launchType === 'string' ? body.launchType : 'DirectExecutable';
  const executablePath = typeof body.executablePath === 'string' ? body.executablePath.trim() : '';
  const launchArguments = typeof body.launchArguments === 'string' ? body.launchArguments.trim() : '';
  const externalAppId = typeof body.externalAppId === 'string' ? body.externalAppId.trim() : '';

  if (!name) return { valid: false, error: 'Game name is required.' };

  const forbidden = ['|', '&', ';', '`', '$', '>', '<', '\r', '\n'];
  for (const ch of forbidden) {
    if (launchArguments.includes(ch)) {
      return { valid: false, error: `Launch arguments contain disallowed shell character '${ch}'.` };
    }
  }

  if (executablePath.includes('..')) {
    return { valid: false, error: "Path traversal ('..') is not allowed." };
  }

  if (launchType === 'Steam' && !/^\d+$/.test(externalAppId)) {
    return { valid: false, error: 'Steam launch type requires a numeric Steam AppID.' };
  }

  if (launchType === 'RiotClient' && !/^[a-zA-Z0-9._-]+$/.test(externalAppId)) {
    return { valid: false, error: 'Riot Client launch type requires a valid product ID (e.g., valorant).' };
  }

  if (launchType === 'DirectExecutable' && !executablePath) {
    return { valid: false, error: 'UNC executable path is required for DirectExecutable launch type.' };
  }

  if (executablePath && executablePath.startsWith('\\\\')) {
    const uncOk = /^\\\\[a-zA-Z0-9._-]+\\[a-zA-Z0-9.$_-]+(?:\\[^<>:"/|?*\r\n]+)*$/.test(executablePath);
    if (!uncOk) {
      return { valid: false, error: 'Invalid UNC path format. Expected \\\\Server\\Share\\Path\\Game.exe.' };
    }
  }

  return { valid: true };
}

function collectSolutionTree(dir: string, relBase = ''): Array<{ path: string; sizeBytes: number }> {
  if (!fs.existsSync(dir)) return [];
  const results: Array<{ path: string; sizeBytes: number }> = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj') continue;
    const rel = relBase ? `${relBase}/${entry.name}` : entry.name;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...collectSolutionTree(full, rel));
    } else if (entry.isFile()) {
      const stat = fs.statSync(full);
      results.push({ path: rel, sizeBytes: stat.size });
    }
  }
  return results.sort((a, b) => a.path.localeCompare(b.path));
}

async function startServer(): Promise<void> {
  const app = express();
  app.use(express.json({ limit: '1mb' }));

  let store = loadStore();

  const appendLog = (action: string, targetGameId: string, targetGameName: string, workstationName: string, details: string) => {
    const nextId = store.auditLogs.length > 0 ? Math.max(...store.auditLogs.map((l) => l.id)) + 1 : 1;
    store.auditLogs.unshift({
      id: nextId,
      timestampUtc: new Date().toISOString(),
      action,
      targetGameId,
      targetGameName,
      workstationName,
      sourceIp: '192.168.10.50',
      details,
    });
    store.auditLogs = store.auditLogs.slice(0, 250);
  };

  const requireAdminAuth = (req: Request, res: Response, next: NextFunction) => {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      res.status(401).json({ error: 'Administrator authentication required.' });
      return;
    }
    const token = authHeader.slice('Bearer '.length).trim();
    const verified = verifyToken(token);
    if (!verified) {
      res.status(401).json({ error: 'Invalid or expired administrator session.' });
      return;
    }
    res.locals.workstation = verified.workstation;
    next();
  };

  // Strictly read-only catalog endpoint for CCBoot clients
  app.get('/api/catalog', (_req, res) => {
    res.json({
      catalogVersion: store.catalogVersion,
      publishedAtUtc: new Date().toISOString(),
      serverHostname: 'CCBOOT-SRV',
      storageLocations: store.storageLocations,
      games: store.games.filter((g) => g.isVisible).sort((a, b) => a.sortOrder - b.sortOrder),
    });
  });

  // Admin status & auth endpoints
  app.get('/api/admin/status', (_req, res) => {
    const envPasscode = process.env.ADMIN_PASSCODE?.trim();
    const isConfigured = Boolean(envPasscode) || Boolean(store.adminPasswordHash);
    res.json({
      isConfigured,
      usesEnvOverride: Boolean(envPasscode),
      workstationAuthorized: true,
      detectedWorkstation: 'ADMIN-PC',
      detectedClientIp: '192.168.10.50',
      totalGames: store.games.length,
      visibleGames: store.games.filter((g) => g.isVisible).length,
      lastBackupUtc: store.lastBackupUtc,
    });
  });

  app.post('/api/admin/setup', (req, res) => {
    const envPasscode = process.env.ADMIN_PASSCODE?.trim();
    if (envPasscode || store.adminPasswordHash) {
      res.status(409).json({ error: 'Administrator password is already configured.' });
      return;
    }
    const password = typeof req.body?.password === 'string' ? req.body.password : '';
    if (password.length < 8) {
      res.status(400).json({ error: 'Administrator password must be at least 8 characters.' });
      return;
    }
    store.adminPasswordHash = hashPassword(password);
    appendLog('ADMIN_INITIAL_SETUP', '', '', 'ADMIN-PC', 'Claimed initial administrator password (scrypt hashed).');
    saveStore(store);
    res.json(issueToken('ADMIN-PC'));
  });

  app.post('/api/admin/login', (req, res) => {
    const password = typeof req.body?.password === 'string' ? req.body.password : '';
    const envPasscode = process.env.ADMIN_PASSCODE?.trim();
    if (!envPasscode && !store.adminPasswordHash) {
      res.status(409).json({ error: 'Administrator password has not been configured yet.' });
      return;
    }

    const ok = envPasscode
      ? verifyEnvPasscode(password, envPasscode)
      : verifyPassword(password, store.adminPasswordHash);

    if (!ok) {
      appendLog('ADMIN_LOGIN_FAILED', '', '', 'ADMIN-PC', 'Rejected invalid administrator password.');
      saveStore(store);
      res.status(401).json({ error: 'Invalid administrator password.' });
      return;
    }

    appendLog('ADMIN_LOGIN_SUCCESS', '', '', 'ADMIN-PC', 'Administrator authenticated on ADMIN-PC.');
    saveStore(store);
    res.json(issueToken('ADMIN-PC'));
  });

  // Protected Admin CRUD routes
  app.get('/api/admin/games', requireAdminAuth, (_req, res) => {
    res.json({
      games: [...store.games].sort((a, b) => a.sortOrder - b.sortOrder),
      storageLocations: store.storageLocations,
    });
  });

  app.post('/api/admin/games', requireAdminAuth, (req, res) => {
    const check = validateGameInput(req.body ?? {});
    if (!check.valid) {
      res.status(400).json({ error: check.error });
      return;
    }

    const now = new Date().toISOString();
    const game: GameEntry = {
      id: crypto.randomUUID(),
      name: String(req.body.name).trim(),
      category: String(req.body.category || 'Action').trim(),
      description: String(req.body.description || '').trim(),
      developer: String(req.body.developer || '').trim(),
      coverImagePath: String(req.body.coverImagePath || '/src/assets/images/cover_valor_protocol_1795374591215.jpg').trim(),
      heroImagePath: String(req.body.heroImagePath || '/src/assets/images/hero_cyber_arena_1795374591030.jpg').trim(),
      storageLocationId: String(req.body.storageLocationId || 'ccboot-primary').trim(),
      executablePath: String(req.body.executablePath || '').trim(),
      launchArguments: String(req.body.launchArguments || '').trim(),
      launchType: (req.body.launchType as LaunchType) || 'DirectExecutable',
      externalAppId: String(req.body.externalAppId || '').trim(),
      processNameToMonitor: String(req.body.processNameToMonitor || '').trim(),
      isVisible: req.body.isVisible !== false,
      isFeatured: Boolean(req.body.isFeatured),
      sortOrder: store.games.length + 1,
      createdAtUtc: now,
      updatedAtUtc: now,
    };

    store.games.push(game);
    store.catalogVersion += 1;
    appendLog('GAME_ADDED', game.id, game.name, res.locals.workstation, `Added ${game.launchType} entry (${game.executablePath || game.externalAppId}).`);
    saveStore(store);
    res.status(201).json(game);
  });

  app.put('/api/admin/games/:id', requireAdminAuth, (req, res) => {
    const idx = store.games.findIndex((g) => g.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Game record not found.' });
      return;
    }
    const check = validateGameInput(req.body ?? {});
    if (!check.valid) {
      res.status(400).json({ error: check.error });
      return;
    }

    const existing = store.games[idx];
    const updated: GameEntry = {
      ...existing,
      name: String(req.body.name).trim(),
      category: String(req.body.category || existing.category).trim(),
      description: String(req.body.description ?? existing.description).trim(),
      developer: String(req.body.developer ?? existing.developer).trim(),
      coverImagePath: String(req.body.coverImagePath || existing.coverImagePath).trim(),
      storageLocationId: String(req.body.storageLocationId || existing.storageLocationId).trim(),
      executablePath: String(req.body.executablePath ?? existing.executablePath).trim(),
      launchArguments: String(req.body.launchArguments ?? existing.launchArguments).trim(),
      launchType: (req.body.launchType as LaunchType) || existing.launchType,
      externalAppId: String(req.body.externalAppId ?? existing.externalAppId).trim(),
      processNameToMonitor: String(req.body.processNameToMonitor ?? existing.processNameToMonitor).trim(),
      isVisible: typeof req.body.isVisible === 'boolean' ? req.body.isVisible : existing.isVisible,
      isFeatured: typeof req.body.isFeatured === 'boolean' ? req.body.isFeatured : existing.isFeatured,
      updatedAtUtc: new Date().toISOString(),
    };

    store.games[idx] = updated;
    store.catalogVersion += 1;
    appendLog('GAME_UPDATED', updated.id, updated.name, res.locals.workstation, `Updated metadata & launch configuration (Visible=${updated.isVisible}).`);
    saveStore(store);
    res.json(updated);
  });

  app.patch('/api/admin/games/:id/visibility', requireAdminAuth, (req, res) => {
    const game = store.games.find((g) => g.id === req.params.id);
    if (!game) {
      res.status(404).json({ error: 'Game record not found.' });
      return;
    }
    const visible = req.query.visible === 'true';
    game.isVisible = visible;
    game.updatedAtUtc = new Date().toISOString();
    store.catalogVersion += 1;
    appendLog(visible ? 'GAME_SHOWN' : 'GAME_HIDDEN', game.id, game.name, res.locals.workstation, `Visibility changed to ${visible}.`);
    saveStore(store);
    res.json(game);
  });

  app.delete('/api/admin/games/:id', requireAdminAuth, (req, res) => {
    const idx = store.games.findIndex((g) => g.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Game record not found.' });
      return;
    }
    const [removed] = store.games.splice(idx, 1);
    store.catalogVersion += 1;
    appendLog(
      'GAME_RECORD_REMOVED',
      removed.id,
      removed.name,
      res.locals.workstation,
      'Removed library record only; UNC game files on CCBoot/SMB storage untouched.',
    );
    saveStore(store);
    res.status(204).send();
  });

  app.get('/api/admin/logs', requireAdminAuth, (_req, res) => {
    res.json(store.auditLogs);
  });

  app.post('/api/admin/backup', requireAdminAuth, (_req, res) => {
    const now = new Date().toISOString();
    store.lastBackupUtc = now;
    appendLog('DATABASE_BACKUP', '', '', res.locals.workstation, 'Created snapshot backup of library store.');
    saveStore(store);
    res.json({ createdAtUtc: now, snapshot: store });
  });

  // Read-only .NET 8 solution source browser endpoints
  app.get('/api/solution/tree', (_req, res) => {
    res.json({ files: collectSolutionTree(DOTNET_ROOT) });
  });

  app.get('/api/solution/file', (req, res) => {
    const relPath = typeof req.query.path === 'string' ? req.query.path : '';
    if (!relPath) {
      res.status(400).json({ error: 'Missing path query parameter.' });
      return;
    }
    const resolved = path.resolve(DOTNET_ROOT, relPath);
    if (!resolved.startsWith(DOTNET_ROOT + path.sep) || !fs.existsSync(resolved) || !fs.statSync(resolved).isFile()) {
      res.status(404).json({ error: 'Solution file not found.' });
      return;
    }
    res.json({
      path: relPath,
      content: fs.readFileSync(resolved, 'utf8'),
    });
  });

  if (process.env.NODE_ENV !== 'production') {
    const { createServer: createViteServer } = await import('vite');
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  } else {
    const distDir = path.join(ROOT_DIR, 'dist');
    app.use(express.static(distDir));
    app.get('*', (_req, res) => {
      res.sendFile(path.join(distDir, 'index.html'));
    });
  }

  const PORT = 3000;
  app.listen(PORT, '0.0.0.0', () => {
    console.log(`CYBER HUB preview server listening on http://0.0.0.0:${PORT}`);
  });
}

startServer().catch((err) => {
  console.error('Failed to start CYBER HUB server:', err);
  process.exit(1);
});
