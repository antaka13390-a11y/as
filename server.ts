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
type NetworkMode = 'Online' | 'Offline';
type CategoryFilterType = 'Custom' | 'AllGames' | 'OnlineGames' | 'OfflineGames';

interface CategoryEntry {
  id: string;
  slug: string;
  nameEn: string;
  nameAr: string;
  iconKey: string;
  bannerImagePath: string;
  displayOrder: number;
  isSystem: boolean;
  filterType: CategoryFilterType;
  createdAtUtc: string;
  updatedAtUtc: string;
}

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
  categoryIds: string[];
  networkMode: NetworkMode;
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

interface ThemeConfig {
  id: string;
  name: string;
  presetKey: string;
  isDefault: boolean;
  isPublished: boolean;
  version: number;
  updatedAtUtc: string;
  backgroundType: 'Solid' | 'Gradient' | 'Image' | 'Video';
  backgroundImageAssetPath: string;
  backgroundVideoAssetPath: string;
  staticFallbackImagePath: string;
  videoBackgroundEnabled: boolean;
  performanceFriendlyMode: boolean;
  solidBackgroundColor: string;
  gradientStartColor: string;
  gradientEndColor: string;
  gradientAngleDegrees: number;
  backgroundBrightness: number;
  backgroundOpacity: number;
  overlayOpacity: number;
  blurIntensity: number;
  imagePosition: string;
  imageScaling: 'Cover' | 'Contain';
  primaryAccentColor: string;
  secondaryAccentColor: string;
  mainBackgroundColor: string;
  sidebarBackgroundColor: string;
  gameCardBackgroundColor: string;
  primaryTextColor: string;
  secondaryTextColor: string;
  buttonBackgroundColor: string;
  buttonTextColor: string;
  hoverStateColor: string;
  borderColor: string;
  activeNavItemColor: string;
  cardLayoutStyle: 'Standard' | 'LargeArtwork' | 'Compact';
  cardShape: 'Rounded' | 'Sharp' | 'CutCorner';
  cardBorderRadius: number;
  cardWidth: number;
  cardHeight: number;
  coverImageHeight: number;
  cardSpacing: number;
  cardShadowIntensity: number;
  hoverAnimation: 'ScaleUp' | 'Glow' | 'Lift' | 'None';
  borderEffect: 'Subtle' | 'NeonGlow' | 'None';
  titleFontSize: number;
  showDescriptionOnCard: boolean;
  playButtonStyle: 'Gradient' | 'Solid' | 'Outline';
  gridColumns: number;
  sidebarPosition: 'Start' | 'Left' | 'Right';
  sidebarWidth: number;
  navigationStyle: 'VerticalRail' | 'CompactIcons' | 'TopTabs';
  topBarAppearance: string;
  searchBarStyle: string;
  gameGridLayout: string;
  categoryNavLayout: string;
  headerSpacing: number;
  mainContentSpacing: number;
  applicationNameEn: string;
  applicationNameAr: string;
  cyberCafeNameEn: string;
  cyberCafeNameAr: string;
  welcomeTitleEn: string;
  welcomeTitleAr: string;
  loadingScreenTitleEn: string;
  loadingScreenTitleAr: string;
  windowTitleEn: string;
  windowTitleAr: string;
  logoAssetPath: string;
  appIconAssetPath: string;
  loadingLogoAssetPath: string;
  logoSizePx: number;
  logoPosition: 'SidebarTop' | 'TopBarStart' | 'TopBarCenter';
}

interface ThemeAssetMetadata {
  id: string;
  fileName: string;
  originalFileName: string;
  mediaKind: string;
  contentType: string;
  sizeBytes: number;
  relativeUrl: string;
  uploadedAtUtc: string;
}

interface ThemeAuditLog {
  id: number;
  timestampUtc: string;
  action: string;
  themeId: string;
  themeName: string;
  workstationName: string;
  sourceIp: string;
  details: string;
}

interface StoreData {
  catalogVersion: number;
  publishedThemeVersion: number;
  activeThemeId: string;
  tokenEpoch: number;
  lastBackupUtc: string;
  adminPasswordHash: string;
  storageLocations: StorageLocation[];
  categories: CategoryEntry[];
  games: GameEntry[];
  themes: ThemeConfig[];
  themeAssets: ThemeAssetMetadata[];
  themeAuditLogs: ThemeAuditLog[];
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

function issueToken(workstation: string, tokenEpoch: number): { accessToken: string; expiresAtUtc: string } {
  const expMs = Date.now() + 8 * 60 * 60 * 1000;
  const payload = Buffer.from(
    JSON.stringify({ sub: 'CyberHubAdmin', workstation, expMs, epoch: tokenEpoch }),
    'utf8',
  ).toString('base64url');
  const sig = crypto.createHmac('sha256', SESSION_SECRET).update(payload).digest('base64url');
  return {
    accessToken: `${payload}.${sig}`,
    expiresAtUtc: new Date(expMs).toISOString(),
  };
}

function verifyToken(token: string, currentEpoch: number): { workstation: string } | null {
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
      epoch?: number;
    };
    if (!parsed.expMs || Date.now() >= parsed.expMs) return null;
    if ((parsed.epoch ?? 0) !== currentEpoch) return null;
    return { workstation: parsed.workstation || 'ADMIN-PC' };
  } catch {
    return null;
  }
}

function baseThemeConfig(now: string, overrides: Partial<ThemeConfig> = {}): ThemeConfig {
  return {
    id: 'theme-neon-purple',
    name: 'Neon Purple',
    presetKey: 'NeonPurple',
    isDefault: false,
    isPublished: false,
    version: 1,
    updatedAtUtc: now,
    backgroundType: 'Gradient',
    backgroundImageAssetPath: '',
    backgroundVideoAssetPath: '',
    staticFallbackImagePath: '',
    videoBackgroundEnabled: false,
    performanceFriendlyMode: true,
    solidBackgroundColor: '#08090D',
    gradientStartColor: '#08090D',
    gradientEndColor: '#19102E',
    gradientAngleDegrees: 135,
    backgroundBrightness: 1.0,
    backgroundOpacity: 1.0,
    overlayOpacity: 0.5,
    blurIntensity: 0,
    imagePosition: 'Center',
    imageScaling: 'Cover',
    primaryAccentColor: '#8B5CF6',
    secondaryAccentColor: '#06B6D4',
    mainBackgroundColor: '#08090D',
    sidebarBackgroundColor: '#0F1118',
    gameCardBackgroundColor: '#11131C',
    primaryTextColor: '#F8FAFC',
    secondaryTextColor: '#94A3B8',
    buttonBackgroundColor: '#8B5CF6',
    buttonTextColor: '#FFFFFF',
    hoverStateColor: '#1B1E2E',
    borderColor: '#252A3D',
    activeNavItemColor: '#2B1E54',
    cardLayoutStyle: 'Standard',
    cardShape: 'Rounded',
    cardBorderRadius: 12,
    cardWidth: 248,
    cardHeight: 368,
    coverImageHeight: 276,
    cardSpacing: 20,
    cardShadowIntensity: 0.35,
    hoverAnimation: 'ScaleUp',
    borderEffect: 'Subtle',
    titleFontSize: 16,
    showDescriptionOnCard: false,
    playButtonStyle: 'Gradient',
    gridColumns: 4,
    sidebarPosition: 'Start',
    sidebarWidth: 264,
    navigationStyle: 'VerticalRail',
    topBarAppearance: 'Elevated',
    searchBarStyle: 'Rounded',
    gameGridLayout: 'FeaturedPlusGrid',
    categoryNavLayout: 'SidebarList',
    headerSpacing: 24,
    mainContentSpacing: 28,
    applicationNameEn: 'CYBER HUB',
    applicationNameAr: 'سايبر هب',
    cyberCafeNameEn: 'CYBER HUB ARENA',
    cyberCafeNameAr: 'صالة سايبر هب',
    welcomeTitleEn: 'Welcome to CYBER HUB',
    welcomeTitleAr: 'مرحباً بكم في سايبر هب',
    loadingScreenTitleEn: 'Loading Library...',
    loadingScreenTitleAr: 'جارٍ تحميل المكتبة...',
    windowTitleEn: 'CYBER HUB',
    windowTitleAr: 'سايبر هب',
    logoAssetPath: '',
    appIconAssetPath: '',
    loadingLogoAssetPath: '',
    logoSizePx: 40,
    logoPosition: 'SidebarTop',
    ...overrides,
  };
}

function defaultThemes(now: string): ThemeConfig[] {
  return [
    baseThemeConfig(now, { id: 'theme-neon-purple', name: 'Neon Purple', presetKey: 'NeonPurple', isDefault: true, isPublished: true }),
    baseThemeConfig(now, {
      id: 'theme-cyber-blue',
      name: 'Cyber Blue',
      presetKey: 'CyberBlue',
      solidBackgroundColor: '#050A12',
      gradientStartColor: '#050A12',
      gradientEndColor: '#0B2239',
      gradientAngleDegrees: 145,
      primaryAccentColor: '#0EA5E9',
      secondaryAccentColor: '#22D3EE',
      mainBackgroundColor: '#050A12',
      sidebarBackgroundColor: '#091320',
      gameCardBackgroundColor: '#0D1A2B',
      primaryTextColor: '#F0F9FF',
      secondaryTextColor: '#93C5FD',
      buttonBackgroundColor: '#0284C7',
      hoverStateColor: '#132942',
      borderColor: '#1D3B5E',
      activeNavItemColor: '#0E3459',
      cardLayoutStyle: 'LargeArtwork',
      cardShape: 'CutCorner',
      cardBorderRadius: 10,
      borderEffect: 'NeonGlow',
    }),
    baseThemeConfig(now, {
      id: 'theme-dark-red',
      name: 'Dark Red',
      presetKey: 'DarkRed',
      solidBackgroundColor: '#0B0709',
      gradientStartColor: '#0B0709',
      gradientEndColor: '#290B12',
      gradientAngleDegrees: 130,
      primaryAccentColor: '#EF4444',
      secondaryAccentColor: '#F97316',
      mainBackgroundColor: '#0B0709',
      sidebarBackgroundColor: '#130B0E',
      gameCardBackgroundColor: '#180E12',
      primaryTextColor: '#FEF2F2',
      secondaryTextColor: '#FCA5A5',
      buttonBackgroundColor: '#DC2626',
      hoverStateColor: '#2A151B',
      borderColor: '#3B1B22',
      activeNavItemColor: '#4A161F',
      cardShape: 'Sharp',
      cardBorderRadius: 4,
      borderEffect: 'NeonGlow',
      playButtonStyle: 'Solid',
    }),
    baseThemeConfig(now, {
      id: 'theme-luxury-gold',
      name: 'Luxury Gold',
      presetKey: 'LuxuryGold',
      solidBackgroundColor: '#0A0908',
      gradientStartColor: '#0A0908',
      gradientEndColor: '#211A0E',
      gradientAngleDegrees: 140,
      primaryAccentColor: '#D4AF37',
      secondaryAccentColor: '#FDE68A',
      mainBackgroundColor: '#0A0908',
      sidebarBackgroundColor: '#12100C',
      gameCardBackgroundColor: '#171410',
      primaryTextColor: '#FAF7F0',
      secondaryTextColor: '#C9B99A',
      buttonBackgroundColor: '#B8860B',
      buttonTextColor: '#0A0908',
      hoverStateColor: '#241F17',
      borderColor: '#3D3220',
      activeNavItemColor: '#382C15',
      cardLayoutStyle: 'LargeArtwork',
      cardBorderRadius: 14,
      playButtonStyle: 'Solid',
    }),
    baseThemeConfig(now, {
      id: 'theme-minimal-dark',
      name: 'Minimal Dark',
      presetKey: 'MinimalDark',
      backgroundType: 'Solid',
      solidBackgroundColor: '#0A0A0C',
      gradientStartColor: '#0A0A0C',
      gradientEndColor: '#0A0A0C',
      primaryAccentColor: '#E2E8F0',
      secondaryAccentColor: '#94A3B8',
      mainBackgroundColor: '#0A0A0C',
      sidebarBackgroundColor: '#111115',
      gameCardBackgroundColor: '#141419',
      primaryTextColor: '#F8FAFC',
      secondaryTextColor: '#94A3B8',
      buttonBackgroundColor: '#E2E8F0',
      buttonTextColor: '#0A090C',
      hoverStateColor: '#1D1D24',
      borderColor: '#24242E',
      activeNavItemColor: '#20202A',
      cardLayoutStyle: 'Compact',
      cardBorderRadius: 8,
      cardSpacing: 14,
      playButtonStyle: 'Outline',
      gridColumns: 5,
    }),
    baseThemeConfig(now, {
      id: 'theme-custom-starter',
      name: 'Custom Theme',
      presetKey: 'Custom',
    }),
  ];
}

function defaultCategories(now: string): CategoryEntry[] {
  return [
    { id: 'cat-all', slug: 'all-games', nameEn: 'All Games', nameAr: 'جميع الألعاب', iconKey: 'grid', bannerImagePath: '', displayOrder: 1, isSystem: true, filterType: 'AllGames', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-online', slug: 'online-games', nameEn: 'Online Games', nameAr: 'ألعاب أونلاين', iconKey: 'globe', bannerImagePath: '', displayOrder: 2, isSystem: true, filterType: 'OnlineGames', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-offline', slug: 'offline-games', nameEn: 'Offline Games', nameAr: 'ألعاب أوفلاين', iconKey: 'hard-drive', bannerImagePath: '', displayOrder: 3, isSystem: true, filterType: 'OfflineGames', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-action', slug: 'action', nameEn: 'Action', nameAr: 'أكشن', iconKey: 'flame', bannerImagePath: '', displayOrder: 4, isSystem: false, filterType: 'Custom', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-shooting', slug: 'shooting', nameEn: 'Shooting', nameAr: 'تصويب', iconKey: 'crosshair', bannerImagePath: '', displayOrder: 5, isSystem: false, filterType: 'Custom', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-sports', slug: 'sports', nameEn: 'Sports', nameAr: 'رياضة', iconKey: 'trophy', bannerImagePath: '', displayOrder: 6, isSystem: false, filterType: 'Custom', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-racing', slug: 'racing', nameEn: 'Racing', nameAr: 'سباقات', iconKey: 'gauge', bannerImagePath: '', displayOrder: 7, isSystem: false, filterType: 'Custom', createdAtUtc: now, updatedAtUtc: now },
    { id: 'cat-adventure', slug: 'adventure', nameEn: 'Adventure', nameAr: 'مغامرات', iconKey: 'compass', bannerImagePath: '', displayOrder: 8, isSystem: false, filterType: 'Custom', createdAtUtc: now, updatedAtUtc: now },
  ];
}

function createInitialStore(): StoreData {
  const now = new Date().toISOString();
  return {
    catalogVersion: 3,
    publishedThemeVersion: 1,
    activeThemeId: 'theme-neon-purple',
    tokenEpoch: 1,
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
    categories: defaultCategories(now),
    games: [
      {
        id: '11111111-1111-4444-8888-100000000001',
        name: 'Valor Protocol: Zero',
        category: 'Shooting',
        categoryIds: ['cat-shooting', 'cat-action'],
        networkMode: 'Online',
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
        category: 'Action',
        categoryIds: ['cat-action'],
        networkMode: 'Online',
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
        category: 'Adventure',
        categoryIds: ['cat-adventure', 'cat-action'],
        networkMode: 'Offline',
        description: 'Dark gothic action adventure stored on the primary CCBoot diskless game volume for instant zero-copy station streaming.',
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
        categoryIds: ['cat-racing', 'cat-sports'],
        networkMode: 'Offline',
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
    themes: defaultThemes(now),
    themeAssets: [],
    themeAuditLogs: [
      {
        id: 1,
        timestampUtc: now,
        action: 'THEME_INITIALIZED',
        themeId: 'theme-neon-purple',
        themeName: 'Neon Purple',
        workstationName: 'CCBOOT-SRV',
        sourceIp: '127.0.0.1',
        details: 'Seeded 6 built-in themes with default Neon Purple CYBER HUB branding.',
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
        details: 'Initialized central game library with dynamic categories and Online/Offline classifications.',
      },
    ],
  };
}

function migrateStoreInPlace(raw: Partial<StoreData>): StoreData {
  const now = new Date().toISOString();
  const base = createInitialStore();
  const categories = Array.isArray(raw.categories) && raw.categories.length > 0
    ? raw.categories
    : defaultCategories(now);

  const games = (raw.games ?? base.games).map((g) => {
    const existingIds = Array.isArray(g.categoryIds) ? g.categoryIds : [];
    const fallbackIds = existingIds.length > 0
      ? existingIds
      : [categories.find((c) => !c.isSystem && c.nameEn.toLowerCase() === (g.category || '').toLowerCase())?.id || 'cat-action'];
    return {
      ...g,
      networkMode: g.networkMode === 'Offline' ? 'Offline' : 'Online',
      categoryIds: fallbackIds,
    } as GameEntry;
  });

  const themes = Array.isArray(raw.themes) && raw.themes.length > 0
    ? raw.themes.map((t) => baseThemeConfig(now, t))
    : defaultThemes(now);
  const publishedTheme = themes.find((t) => t.isPublished) ?? themes.find((t) => t.isDefault) ?? themes[0];

  return {
    catalogVersion: raw.catalogVersion ?? 3,
    publishedThemeVersion: raw.publishedThemeVersion ?? 1,
    activeThemeId: raw.activeThemeId || publishedTheme.id,
    tokenEpoch: Number.isFinite(Number(raw.tokenEpoch)) && Number(raw.tokenEpoch) >= 1 ? Number(raw.tokenEpoch) : 1,
    lastBackupUtc: raw.lastBackupUtc ?? '',
    adminPasswordHash: raw.adminPasswordHash ?? '',
    storageLocations: raw.storageLocations ?? base.storageLocations,
    categories,
    games,
    themes,
    themeAssets: Array.isArray(raw.themeAssets) ? raw.themeAssets : [],
    themeAuditLogs: Array.isArray(raw.themeAuditLogs) && raw.themeAuditLogs.length > 0
      ? raw.themeAuditLogs
      : base.themeAuditLogs,
    auditLogs: raw.auditLogs ?? base.auditLogs,
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
    const parsed = JSON.parse(fs.readFileSync(STORE_PATH, 'utf8')) as Partial<StoreData>;
    const migrated = migrateStoreInPlace(parsed);
    saveStore(migrated);
    return migrated;
  } catch {
    const initial = createInitialStore();
    saveStore(initial);
    return initial;
  }
}

function saveStore(store: StoreData): void {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = `${STORE_PATH}.${crypto.randomUUID()}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(store, null, 2), 'utf8');
  fs.renameSync(tmp, STORE_PATH);
}

function validateGameInput(body: Record<string, unknown>, validCategoryIds: Set<string>): { valid: boolean; error?: string } {
  const name = typeof body.name === 'string' ? body.name.trim() : '';
  const launchType = typeof body.launchType === 'string' ? body.launchType : 'DirectExecutable';
  const executablePath = typeof body.executablePath === 'string' ? body.executablePath.trim() : '';
  const launchArguments = typeof body.launchArguments === 'string' ? body.launchArguments.trim() : '';
  const externalAppId = typeof body.externalAppId === 'string' ? body.externalAppId.trim() : '';
  const categoryIds = Array.isArray(body.categoryIds) ? body.categoryIds : [];

  if (!name) return { valid: false, error: 'Game name is required.' };

  for (const cid of categoryIds) {
    if (typeof cid !== 'string' || !validCategoryIds.has(cid)) {
      return { valid: false, error: `Invalid category reference '${String(cid)}'.` };
    }
  }

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

  const appendThemeLog = (action: string, themeId: string, themeName: string, workstationName: string, details: string) => {
    const nextId = store.themeAuditLogs.length > 0 ? Math.max(...store.themeAuditLogs.map((l) => l.id)) + 1 : 1;
    store.themeAuditLogs.unshift({
      id: nextId,
      timestampUtc: new Date().toISOString(),
      action,
      themeId,
      themeName,
      workstationName,
      sourceIp: '192.168.10.50',
      details,
    });
    store.themeAuditLogs = store.themeAuditLogs.slice(0, 250);
  };

  const getPublishedThemePayload = () => {
    const published =
      store.themes.find((t) => t.isPublished) ??
      store.themes.find((t) => t.id === store.activeThemeId) ??
      store.themes[0] ??
      baseThemeConfig(new Date().toISOString(), { isDefault: true, isPublished: true });
    return {
      publishedVersion: store.publishedThemeVersion,
      publishedAtUtc: published.updatedAtUtc || new Date().toISOString(),
      theme: published,
      assets: store.themeAssets,
    };
  };

  const requireAdminAuth = (req: Request, res: Response, next: NextFunction) => {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      res.status(401).json({ error: 'Administrator authentication required.' });
      return;
    }
    const token = authHeader.slice('Bearer '.length).trim();
    const verified = verifyToken(token, store.tokenEpoch);
    if (!verified) {
      res.status(401).json({ error: 'Invalid, revoked, or expired administrator session.' });
      return;
    }
    res.locals.workstation = verified.workstation;
    next();
  };

  // Strictly read-only catalog & theme endpoints for CCBoot clients
  app.get('/api/catalog', (_req, res) => {
    res.json({
      catalogVersion: store.catalogVersion,
      publishedAtUtc: new Date().toISOString(),
      serverHostname: 'CCBOOT-SRV',
      storageLocations: store.storageLocations,
      categories: [...store.categories].sort((a, b) => a.displayOrder - b.displayOrder),
      games: store.games.filter((g) => g.isVisible).sort((a, b) => a.sortOrder - b.sortOrder),
      publishedTheme: getPublishedThemePayload(),
    });
  });

  app.get('/api/catalog/theme', (_req, res) => {
    res.json(getPublishedThemePayload());
  });

  app.post('/api/catalog/verify-recovery', (req, res) => {
    const stationName = typeof req.body?.stationName === 'string' ? req.body.stationName.trim().toUpperCase() : '';
    const recoveryCode = typeof req.body?.recoveryCode === 'string' ? req.body.recoveryCode.trim() : '';
    const parts = recoveryCode.split('-');
    if (parts.length !== 2) {
      res.status(401).json({ authorized: false });
      return;
    }
    const expSec = Number(parts[0]);
    if (!Number.isFinite(expSec) || Math.floor(Date.now() / 1000) > expSec) {
      res.status(401).json({ authorized: false });
      return;
    }
    const expectedSig = crypto
      .createHmac('sha256', SESSION_SECRET)
      .update(`${stationName}:${expSec}`)
      .digest('hex')
      .slice(0, 12)
      .toUpperCase();
    const a = Buffer.from(parts[1].toUpperCase(), 'utf8');
    const b = Buffer.from(expectedSig, 'utf8');
    if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) {
      res.status(401).json({ authorized: false });
      return;
    }
    res.json({ authorized: true });
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
      totalCategories: store.categories.length,
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
    res.json(issueToken('ADMIN-PC', store.tokenEpoch));
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
    res.json(issueToken('ADMIN-PC', store.tokenEpoch));
  });

  app.post('/api/admin/change-password', requireAdminAuth, (req, res) => {
    const envPasscode = process.env.ADMIN_PASSCODE?.trim();
    if (envPasscode) {
      res.status(400).json({ error: 'ADMIN_PASSCODE environment override is active; update it in environment secrets.' });
      return;
    }
    const currentPassword = typeof req.body?.currentPassword === 'string' ? req.body.currentPassword : '';
    const newPassword = typeof req.body?.newPassword === 'string' ? req.body.newPassword : '';
    const confirmNewPassword = typeof req.body?.confirmNewPassword === 'string' ? req.body.confirmNewPassword : '';

    if (!store.adminPasswordHash || !verifyPassword(currentPassword, store.adminPasswordHash)) {
      appendLog('ADMIN_PASSWORD_CHANGE_FAILED', '', '', res.locals.workstation, 'Rejected password change due to invalid current password.');
      saveStore(store);
      res.status(400).json({ error: 'Current administrator password is incorrect.' });
      return;
    }
    if (newPassword.length < 10) {
      res.status(400).json({ error: 'New administrator password must be at least 10 characters long.' });
      return;
    }
    if (newPassword !== confirmNewPassword) {
      res.status(400).json({ error: 'New password and confirmation do not match.' });
      return;
    }

    store.adminPasswordHash = hashPassword(newPassword);
    store.tokenEpoch += 1;
    appendLog('ADMIN_PASSWORD_CHANGED', '', '', res.locals.workstation, 'Changed administrator password and revoked all active session tokens.');
    saveStore(store);
    res.json({
      success: true,
      requiresRelogin: true,
      message: 'Password updated. All active sessions have been revoked.',
    });
  });

  app.post('/api/admin/logout', requireAdminAuth, (_req, res) => {
    store.tokenEpoch += 1;
    appendLog('ADMIN_LOGOUT', '', '', res.locals.workstation, 'Administrator logged out cleanly; active tokens revoked.');
    saveStore(store);
    res.json({ success: true });
  });

  // Protected Admin Category CRUD
  app.get('/api/admin/categories', requireAdminAuth, (_req, res) => {
    res.json([...store.categories].sort((a, b) => a.displayOrder - b.displayOrder));
  });

  app.post('/api/admin/categories', requireAdminAuth, (req, res) => {
    const nameEn = typeof req.body?.nameEn === 'string' ? req.body.nameEn.trim() : '';
    const nameAr = typeof req.body?.nameAr === 'string' ? req.body.nameAr.trim() : '';
    if (!nameEn || !nameAr || nameEn.length > 64 || nameAr.length > 64) {
      res.status(400).json({ error: 'Both English and Arabic category names (max 64 chars) are required.' });
      return;
    }
    const now = new Date().toISOString();
    const cat: CategoryEntry = {
      id: `cat-${crypto.randomUUID().slice(0, 8)}`,
      slug: nameEn.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || `cat-${Date.now()}`,
      nameEn,
      nameAr,
      iconKey: typeof req.body?.iconKey === 'string' && req.body.iconKey.trim() ? req.body.iconKey.trim() : 'gamepad',
      bannerImagePath: typeof req.body?.bannerImagePath === 'string' ? req.body.bannerImagePath.trim() : '',
      displayOrder: Number.isFinite(Number(req.body?.displayOrder)) ? Number(req.body.displayOrder) : store.categories.length + 1,
      isSystem: false,
      filterType: 'Custom',
      createdAtUtc: now,
      updatedAtUtc: now,
    };
    store.categories.push(cat);
    store.catalogVersion += 1;
    appendLog('CATEGORY_CREATED', cat.id, cat.nameEn, res.locals.workstation, `Created category '${cat.nameEn}' / '${cat.nameAr}'.`);
    saveStore(store);
    res.status(201).json(cat);
  });

  app.put('/api/admin/categories/:id', requireAdminAuth, (req, res) => {
    const cat = store.categories.find((c) => c.id === req.params.id);
    if (!cat) {
      res.status(404).json({ error: 'Category not found.' });
      return;
    }
    const nameEn = typeof req.body?.nameEn === 'string' ? req.body.nameEn.trim() : '';
    const nameAr = typeof req.body?.nameAr === 'string' ? req.body.nameAr.trim() : '';
    if (!nameEn || !nameAr) {
      res.status(400).json({ error: 'Both English and Arabic category names are required.' });
      return;
    }
    cat.nameEn = nameEn;
    cat.nameAr = nameAr;
    cat.iconKey = typeof req.body?.iconKey === 'string' && req.body.iconKey.trim() ? req.body.iconKey.trim() : cat.iconKey;
    cat.bannerImagePath = typeof req.body?.bannerImagePath === 'string' ? req.body.bannerImagePath.trim() : cat.bannerImagePath;
    cat.displayOrder = Number.isFinite(Number(req.body?.displayOrder)) ? Number(req.body.displayOrder) : cat.displayOrder;
    cat.updatedAtUtc = new Date().toISOString();
    store.catalogVersion += 1;
    appendLog('CATEGORY_UPDATED', cat.id, cat.nameEn, res.locals.workstation, `Updated category '${cat.nameEn}' / '${cat.nameAr}'.`);
    saveStore(store);
    res.json(cat);
  });

  app.delete('/api/admin/categories/:id', requireAdminAuth, (req, res) => {
    const idx = store.categories.findIndex((c) => c.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Category not found.' });
      return;
    }
    if (store.categories[idx].isSystem) {
      res.status(400).json({ error: 'Built-in system categories cannot be deleted.' });
      return;
    }
    const [removed] = store.categories.splice(idx, 1);
    for (const game of store.games) {
      game.categoryIds = game.categoryIds.filter((id) => id !== removed.id);
    }
    store.catalogVersion += 1;
    appendLog('CATEGORY_DELETED', removed.id, removed.nameEn, res.locals.workstation, `Safely removed category '${removed.nameEn}' without deleting games.`);
    saveStore(store);
    res.status(204).send();
  });

  // Protected Admin Games CRUD
  app.get('/api/admin/games', requireAdminAuth, (_req, res) => {
    res.json({
      games: [...store.games].sort((a, b) => a.sortOrder - b.sortOrder),
      categories: [...store.categories].sort((a, b) => a.displayOrder - b.displayOrder),
      storageLocations: store.storageLocations,
    });
  });

  app.post('/api/admin/games', requireAdminAuth, (req, res) => {
    const validIds = new Set(store.categories.filter((c) => !c.isSystem).map((c) => c.id));
    const check = validateGameInput(req.body ?? {}, validIds);
    if (!check.valid) {
      res.status(400).json({ error: check.error });
      return;
    }

    const categoryIds = Array.isArray(req.body.categoryIds) ? (req.body.categoryIds as string[]) : [];
    const primaryCat = store.categories.find((c) => c.id === categoryIds[0])?.nameEn || String(req.body.category || 'Action').trim();
    const now = new Date().toISOString();

    const game: GameEntry = {
      id: crypto.randomUUID(),
      name: String(req.body.name).trim(),
      category: primaryCat,
      categoryIds,
      networkMode: req.body.networkMode === 'Offline' ? 'Offline' : 'Online',
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
    appendLog('GAME_ADDED', game.id, game.name, res.locals.workstation, `Added ${game.networkMode} ${game.launchType} entry.`);
    saveStore(store);
    res.status(201).json(game);
  });

  app.put('/api/admin/games/:id', requireAdminAuth, (req, res) => {
    const idx = store.games.findIndex((g) => g.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Game record not found.' });
      return;
    }
    const validIds = new Set(store.categories.filter((c) => !c.isSystem).map((c) => c.id));
    const check = validateGameInput(req.body ?? {}, validIds);
    if (!check.valid) {
      res.status(400).json({ error: check.error });
      return;
    }

    const existing = store.games[idx];
    const categoryIds = Array.isArray(req.body.categoryIds) ? (req.body.categoryIds as string[]) : existing.categoryIds;
    const primaryCat = store.categories.find((c) => c.id === categoryIds[0])?.nameEn || existing.category;

    const updated: GameEntry = {
      ...existing,
      name: String(req.body.name).trim(),
      category: primaryCat,
      categoryIds,
      networkMode: req.body.networkMode === 'Offline' ? 'Offline' : 'Online',
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
    appendLog('GAME_UPDATED', updated.id, updated.name, res.locals.workstation, `Updated metadata (${updated.networkMode}, Visible=${updated.isVisible}).`);
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

  app.post('/api/admin/recovery-code', requireAdminAuth, (req, res) => {
    const stationName = typeof req.query.stationName === 'string' && req.query.stationName.trim()
      ? req.query.stationName.trim().toUpperCase()
      : 'PC-01';
    const expSec = Math.floor(Date.now() / 1000) + 15 * 60;
    const sig = crypto
      .createHmac('sha256', SESSION_SECRET)
      .update(`${stationName}:${expSec}`)
      .digest('hex')
      .slice(0, 12)
      .toUpperCase();
    const recoveryCode = `${expSec}-${sig}`;
    appendLog('SESSION_RECOVERY_ISSUED', '', '', res.locals.workstation, `Issued 15-minute recovery code for ${stationName}.`);
    saveStore(store);
    res.json({ stationName, recoveryCode, expiresAtUtc: new Date(expSec * 1000).toISOString() });
  });

  // Protected Admin Themes & Branding CRUD / Publishing
  const isHex = (v: unknown): v is string => typeof v === 'string' && /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/.test(v.trim());
  const sanitizeThemePatch = (base: ThemeConfig, body: Record<string, unknown>): ThemeConfig => {
    const merged: ThemeConfig = {
      ...base,
      ...body,
      id: base.id,
      isDefault: base.isDefault,
      isPublished: base.isPublished,
      version: base.version + 1,
      updatedAtUtc: new Date().toISOString(),
    };
    const hexFields = [
      'solidBackgroundColor',
      'gradientStartColor',
      'gradientEndColor',
      'primaryAccentColor',
      'secondaryAccentColor',
      'mainBackgroundColor',
      'sidebarBackgroundColor',
      'gameCardBackgroundColor',
      'primaryTextColor',
      'secondaryTextColor',
      'buttonBackgroundColor',
      'buttonTextColor',
      'hoverStateColor',
      'borderColor',
      'activeNavItemColor',
    ] as const;
    for (const key of hexFields) {
      if (!isHex(merged[key])) {
        merged[key] = base[key];
      }
    }
    merged.gridColumns = Math.min(8, Math.max(2, Number(merged.gridColumns) || base.gridColumns));
    merged.cardBorderRadius = Math.min(32, Math.max(0, Number(merged.cardBorderRadius) ?? base.cardBorderRadius));
    merged.sidebarWidth = Math.min(360, Math.max(180, Number(merged.sidebarWidth) || base.sidebarWidth));
    return merged;
  };

  app.get('/api/admin/themes', requireAdminAuth, (_req, res) => {
    res.json({
      activeThemeId: store.activeThemeId,
      publishedVersion: store.publishedThemeVersion,
      themes: store.themes,
      assets: store.themeAssets,
      auditLogs: store.themeAuditLogs,
    });
  });

  app.post('/api/admin/themes', requireAdminAuth, (req, res) => {
    const name = typeof req.body?.name === 'string' && req.body.name.trim() ? req.body.name.trim() : 'Custom Theme';
    const created = sanitizeThemePatch(
      baseThemeConfig(new Date().toISOString(), {
        id: `theme-${crypto.randomUUID().slice(0, 8)}`,
        name,
        presetKey: 'Custom',
      }),
      (req.body ?? {}) as Record<string, unknown>,
    );
    created.name = name;
    store.themes.push(created);
    appendThemeLog('THEME_CREATED', created.id, created.name, res.locals.workstation, `Created theme '${created.name}'.`);
    saveStore(store);
    res.status(201).json(created);
  });

  app.put('/api/admin/themes/:id', requireAdminAuth, (req, res) => {
    const idx = store.themes.findIndex((t) => t.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Theme not found.' });
      return;
    }
    const existing = store.themes[idx];
    const updated = sanitizeThemePatch(existing, (req.body ?? {}) as Record<string, unknown>);
    if (typeof req.body?.name === 'string' && req.body.name.trim()) {
      updated.name = req.body.name.trim();
    }
    store.themes[idx] = updated;
    appendThemeLog('THEME_UPDATED', updated.id, updated.name, res.locals.workstation, 'Saved theme draft changes.');
    saveStore(store);
    res.json(updated);
  });

  app.post('/api/admin/themes/:id/duplicate', requireAdminAuth, (req, res) => {
    const source = store.themes.find((t) => t.id === req.params.id);
    if (!source) {
      res.status(404).json({ error: 'Theme not found.' });
      return;
    }
    const newName = typeof req.body?.newName === 'string' && req.body.newName.trim()
      ? req.body.newName.trim()
      : `${source.name} Copy`;
    const copy: ThemeConfig = {
      ...source,
      id: `theme-${crypto.randomUUID().slice(0, 8)}`,
      name: newName,
      isDefault: false,
      isPublished: false,
      version: 1,
      updatedAtUtc: new Date().toISOString(),
    };
    store.themes.push(copy);
    appendThemeLog('THEME_DUPLICATED', copy.id, copy.name, res.locals.workstation, `Duplicated from '${source.name}'.`);
    saveStore(store);
    res.status(201).json(copy);
  });

  app.patch('/api/admin/themes/:id/rename', requireAdminAuth, (req, res) => {
    const theme = store.themes.find((t) => t.id === req.params.id);
    if (!theme) {
      res.status(404).json({ error: 'Theme not found.' });
      return;
    }
    const newName = typeof req.body?.newName === 'string' ? req.body.newName.trim() : '';
    if (!newName || newName.length > 64) {
      res.status(400).json({ error: 'Valid theme name (max 64 characters) is required.' });
      return;
    }
    theme.name = newName;
    theme.updatedAtUtc = new Date().toISOString();
    appendThemeLog('THEME_RENAMED', theme.id, theme.name, res.locals.workstation, `Renamed theme to '${newName}'.`);
    saveStore(store);
    res.json(theme);
  });

  app.delete('/api/admin/themes/:id', requireAdminAuth, (req, res) => {
    const idx = store.themes.findIndex((t) => t.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Theme not found.' });
      return;
    }
    if (store.themes[idx].isDefault || store.themes[idx].isPublished) {
      res.status(400).json({ error: 'Cannot delete the active default or published theme.' });
      return;
    }
    const [removed] = store.themes.splice(idx, 1);
    appendThemeLog('THEME_DELETED', removed.id, removed.name, res.locals.workstation, `Deleted theme '${removed.name}'.`);
    saveStore(store);
    res.status(204).send();
  });

  app.post('/api/admin/themes/:id/publish', requireAdminAuth, (req, res) => {
    const target = store.themes.find((t) => t.id === req.params.id);
    if (!target) {
      res.status(404).json({ error: 'Theme not found.' });
      return;
    }
    for (const t of store.themes) {
      t.isDefault = t.id === target.id;
      t.isPublished = t.id === target.id;
    }
    target.updatedAtUtc = new Date().toISOString();
    store.activeThemeId = target.id;
    store.publishedThemeVersion += 1;
    store.catalogVersion += 1;
    appendThemeLog('THEME_PUBLISHED', target.id, target.name, res.locals.workstation, `Published theme v${store.publishedThemeVersion} to CCBoot clients.`);
    saveStore(store);
    res.json(getPublishedThemePayload());
  });

  app.post('/api/admin/themes/restore-default', requireAdminAuth, (_req, res) => {
    const now = new Date().toISOString();
    const freshNeon = baseThemeConfig(now, { id: 'theme-neon-purple', name: 'Neon Purple', presetKey: 'NeonPurple', isDefault: true, isPublished: true });
    const idx = store.themes.findIndex((t) => t.id === 'theme-neon-purple');
    if (idx >= 0) {
      store.themes[idx] = freshNeon;
    } else {
      store.themes.unshift(freshNeon);
    }
    for (const t of store.themes) {
      t.isDefault = t.id === 'theme-neon-purple';
      t.isPublished = t.id === 'theme-neon-purple';
    }
    store.activeThemeId = 'theme-neon-purple';
    store.publishedThemeVersion += 1;
    store.catalogVersion += 1;
    appendThemeLog('THEME_RESTORED_DEFAULT', freshNeon.id, freshNeon.name, res.locals.workstation, 'Restored default Neon Purple CYBER HUB theme.');
    saveStore(store);
    res.json(getPublishedThemePayload());
  });

  app.post('/api/admin/themes/restore-branding', requireAdminAuth, (_req, res) => {
    const active = store.themes.find((t) => t.id === store.activeThemeId) ?? store.themes.find((t) => t.isPublished) ?? store.themes[0];
    if (active) {
      active.applicationNameEn = 'CYBER HUB';
      active.applicationNameAr = 'سايبر هب';
      active.cyberCafeNameEn = 'CYBER HUB ARENA';
      active.cyberCafeNameAr = 'صالة سايبر هب';
      active.welcomeTitleEn = 'Welcome to CYBER HUB';
      active.welcomeTitleAr = 'مرحباً بكم في سايبر هب';
      active.loadingScreenTitleEn = 'Loading Library...';
      active.loadingScreenTitleAr = 'جارٍ تحميل المكتبة...';
      active.windowTitleEn = 'CYBER HUB';
      active.windowTitleAr = 'سايبر هب';
      active.logoAssetPath = '';
      active.appIconAssetPath = '';
      active.loadingLogoAssetPath = '';
      active.updatedAtUtc = new Date().toISOString();
      store.publishedThemeVersion += 1;
      store.catalogVersion += 1;
      appendThemeLog('BRANDING_RESTORED', active.id, active.name, res.locals.workstation, 'Restored original CYBER HUB application branding.');
      saveStore(store);
    }
    res.json(getPublishedThemePayload());
  });

  // Protected Admin Part 11 Storage Locations & Game Scanner APIs
  app.get('/api/admin/scanner/storage-locations', requireAdminAuth, (_req, res) => {
    res.json(store.storageLocations);
  });

  app.post('/api/admin/scanner/storage-locations', requireAdminAuth, (req, res) => {
    const displayName = typeof req.body?.displayName === 'string' ? req.body.displayName.trim() : '';
    const uncRootPath = typeof req.body?.uncRootPath === 'string' ? req.body.uncRootPath.trim() : '';
    if (!displayName || !uncRootPath || uncRootPath.includes('..')) {
      res.status(400).json({ error: 'Valid storage displayName and root path (no traversal) are required.' });
      return;
    }
    const id = typeof req.body?.id === 'string' && req.body.id.trim()
      ? req.body.id.trim()
      : `loc-${crypto.randomUUID().slice(0, 8)}`;
    const entry: StorageLocation = {
      id,
      displayName,
      uncRootPath,
      description: typeof req.body?.description === 'string' ? req.body.description.trim() : '',
      isReadOnlyForClients: req.body?.isReadOnlyForClients !== false,
    };
    const existingIdx = store.storageLocations.findIndex((s) => s.id === id);
    if (existingIdx >= 0) {
      store.storageLocations[existingIdx] = entry;
    } else {
      store.storageLocations.push(entry);
    }
    appendLog('STORAGE_LOCATION_SAVED', entry.id, entry.displayName, res.locals.workstation, `Configured storage path ${entry.uncRootPath}`);
    saveStore(store);
    res.json(entry);
  });

  app.delete('/api/admin/scanner/storage-locations/:id', requireAdminAuth, (req, res) => {
    const idx = store.storageLocations.findIndex((s) => s.id === req.params.id);
    if (idx === -1) {
      res.status(404).json({ error: 'Storage location not found.' });
      return;
    }
    const [removed] = store.storageLocations.splice(idx, 1);
    appendLog('STORAGE_LOCATION_REMOVED', removed.id, removed.displayName, res.locals.workstation, `Removed storage configuration ${removed.uncRootPath}`);
    saveStore(store);
    res.status(204).send();
  });

  app.post('/api/admin/scanner/cover-search', requireAdminAuth, (req, res) => {
    const gameTitle = typeof req.body?.gameTitle === 'string' ? req.body.gameTitle.trim() : '';
    if (!gameTitle) {
      res.status(400).json({ error: 'Game title is required for artwork search.' });
      return;
    }
    res.json({
      queryTitle: gameTitle,
      metadata: {
        officialTitle: gameTitle,
        shortDescription: '',
        releaseYear: '',
        genre: '',
        developer: '',
        platform: 'PC',
        heroImageUrl: '',
      },
      candidates: [],
    });
  });

  app.post('/api/admin/scanner/import', requireAdminAuth, (req, res) => {
    const items = Array.isArray(req.body?.items) ? (req.body.items as Array<Record<string, unknown>>) : [];
    const validIds = new Set(store.categories.filter((c) => !c.isSystem).map((c) => c.id));
    const importedGames: GameEntry[] = [];
    const skippedReasons: string[] = [];

    for (const raw of items) {
      const name = typeof raw.name === 'string' ? raw.name.trim() : '';
      const executablePath = typeof raw.executablePath === 'string' ? raw.executablePath.trim() : '';
      const overwriteExisting = Boolean(raw.overwriteExisting);
      if (!name) {
        skippedReasons.push('Skipped item with empty game name.');
        continue;
      }
      const existing = store.games.find(
        (g) =>
          (executablePath && g.executablePath.toLowerCase() === executablePath.toLowerCase()) ||
          g.name.toLowerCase() === name.toLowerCase(),
      );
      if (existing && !overwriteExisting) {
        skippedReasons.push(`Preserved existing admin-edited record '${existing.name}'.`);
        continue;
      }
      const check = validateGameInput(raw, validIds);
      if (!check.valid) {
        skippedReasons.push(`${name}: ${check.error}`);
        continue;
      }
      const categoryIds = Array.isArray(raw.categoryIds) ? (raw.categoryIds as string[]) : [];
      const primaryCat = store.categories.find((c) => c.id === categoryIds[0])?.nameEn || 'Action';
      const now = new Date().toISOString();
      const created: GameEntry = {
        id: existing ? existing.id : crypto.randomUUID(),
        name,
        category: primaryCat,
        categoryIds,
        networkMode: raw.networkMode === 'Offline' ? 'Offline' : 'Online',
        description: typeof raw.description === 'string' ? raw.description.trim() : '',
        developer: typeof raw.developer === 'string' ? raw.developer.trim() : '',
        coverImagePath: typeof raw.selectedCoverUrl === 'string' && raw.selectedCoverUrl.trim()
          ? raw.selectedCoverUrl.trim()
          : '/src/assets/images/cover_valor_protocol_1795374591215.jpg',
        heroImagePath: typeof raw.heroImagePath === 'string' && raw.heroImagePath.trim()
          ? raw.heroImagePath.trim()
          : '/src/assets/images/hero_cyber_arena_1795374591030.jpg',
        storageLocationId: typeof raw.storageLocationId === 'string' ? raw.storageLocationId : 'ccboot-primary',
        executablePath,
        launchArguments: typeof raw.launchArguments === 'string' ? raw.launchArguments.trim() : '',
        launchType: (raw.launchType as LaunchType) || 'DirectExecutable',
        externalAppId: typeof raw.externalAppId === 'string' ? raw.externalAppId.trim() : '',
        processNameToMonitor: typeof raw.processNameToMonitor === 'string' ? raw.processNameToMonitor.trim() : '',
        isVisible: true,
        isFeatured: false,
        sortOrder: existing ? existing.sortOrder : store.games.length + 1,
        createdAtUtc: existing ? existing.createdAtUtc : now,
        updatedAtUtc: now,
      };
      if (existing) {
        const idx = store.games.findIndex((g) => g.id === existing.id);
        store.games[idx] = created;
      } else {
        store.games.push(created);
      }
      importedGames.push(created);
    }

    if (importedGames.length > 0) {
      store.catalogVersion += 1;
      appendLog('GAMES_SCANNED_IMPORTED', '', '', res.locals.workstation, `Confirmed import of ${importedGames.length} scanned game(s).`);
      saveStore(store);
    }

    res.json({
      importedCount: importedGames.length,
      skippedCount: skippedReasons.length,
      games: importedGames,
      skippedReasons,
    });
  });

  // Solution files remain preserved on disk
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
