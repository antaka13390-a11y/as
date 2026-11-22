import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import {
  Clock,
  Compass,
  Crosshair,
  Flame,
  Gamepad2,
  Gauge,
  Globe,
  HardDrive,
  Home,
  Languages,
  LayoutGrid,
  Lock,
  Play,
  RefreshCw,
  Search,
  Trophy,
  X,
} from 'lucide-react';
import { TRANSLATIONS } from '../i18n/translations';
import type {
  CatalogSnapshot,
  CategoryEntry,
  GameEntry,
  LanguageCode,
  PublishedThemePayload,
  ThemeConfig,
} from '../types';

type NavMode = 'home' | 'recent' | 'category';

interface ClientLauncherViewProps {
  language: LanguageCode;
  onToggleLanguage: () => void;
}

const FALLBACK_HERO = '/src/assets/images/hero_cyber_arena_1795374591030.jpg';

const DEFAULT_THEME: ThemeConfig = {
  id: 'theme-neon-purple',
  name: 'Neon Purple',
  presetKey: 'NeonPurple',
  isDefault: true,
  isPublished: true,
  version: 1,
  updatedAtUtc: '',
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
  backgroundBrightness: 1,
  backgroundOpacity: 1,
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
};

function CategoryIcon({ iconKey }: { iconKey: string }) {
  const cls = 'w-4 h-4 shrink-0 text-[var(--ch-accent-secondary)]';
  switch (iconKey) {
    case 'grid':
      return <LayoutGrid className={cls} />;
    case 'globe':
      return <Globe className={cls} />;
    case 'hard-drive':
      return <HardDrive className={cls} />;
    case 'flame':
      return <Flame className={cls} />;
    case 'crosshair':
      return <Crosshair className={cls} />;
    case 'trophy':
      return <Trophy className={cls} />;
    case 'gauge':
      return <Gauge className={cls} />;
    case 'compass':
      return <Compass className={cls} />;
    default:
      return <Gamepad2 className={cls} />;
  }
}

export default function ClientLauncherView({
  language,
  onToggleLanguage,
}: ClientLauncherViewProps) {
  const t = TRANSLATIONS[language];
  const isRtl = language === 'ar';

  const [catalog, setCatalog] = useState<CatalogSnapshot | null>(null);
  const [publishedTheme, setPublishedTheme] = useState<ThemeConfig>(DEFAULT_THEME);
  const [loading, setLoading] = useState(true);
  const [offlineWarning, setOfflineWarning] = useState<string | null>(null);
  const [navMode, setNavMode] = useState<NavMode>('home');
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>('cat-all');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedGame, setSelectedGame] = useState<GameEntry | null>(null);
  const [recentPlays, setRecentPlays] = useState<Array<{ id: string }>>([
    { id: '11111111-1111-4444-8888-100000000001' },
  ]);

  // Single-game session lock enforced in launch gate ref + state
  const sessionLockRef = useRef(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [activeGameSession, setActiveGameSession] = useState<{
    game: GameEntry;
    commandLine: string;
  } | null>(null);
  const [launchFeedback, setLaunchFeedback] = useState<string | null>(null);

  const loadCatalogAndTheme = async (silent = false) => {
    if (!silent) {
      setLoading(true);
      setOfflineWarning(null);
    }
    try {
      const [catalogResp, themeResp] = await Promise.all([
        fetch('/api/catalog'),
        fetch('/api/catalog/theme'),
      ]);
      if (!catalogResp.ok) {
        throw new Error(`HTTP ${catalogResp.status}`);
      }
      const data = (await catalogResp.json()) as CatalogSnapshot;
      setCatalog(data);
      if (themeResp.ok) {
        const themePayload = (await themeResp.json()) as PublishedThemePayload;
        if (themePayload?.theme) {
          setPublishedTheme(themePayload.theme);
        }
      } else if (data.publishedTheme?.theme) {
        setPublishedTheme(data.publishedTheme.theme);
      }
      setOfflineWarning(null);
    } catch {
      if (!silent) {
        setOfflineWarning(t.offlineWarning);
      }
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  };

  useEffect(() => {
    void loadCatalogAndTheme(false);
    const interval = window.setInterval(() => {
      void loadCatalogAndTheme(true);
    }, 30000);
    return () => window.clearInterval(interval);
  }, []);

  // Dynamically update document title when branding changes
  useEffect(() => {
    const winTitle = isRtl
      ? (publishedTheme.windowTitleAr || publishedTheme.applicationNameAr || t.brand)
      : (publishedTheme.windowTitleEn || publishedTheme.applicationNameEn || t.brand);
    document.title = winTitle;
  }, [isRtl, publishedTheme, t.brand]);

  // Performance-friendly mode: pause video background while a game is running
  useEffect(() => {
    const videoEl = videoRef.current;
    if (!videoEl) return;
    if (activeGameSession && publishedTheme.performanceFriendlyMode) {
      videoEl.pause();
    } else if (publishedTheme.backgroundType === 'Video' && publishedTheme.videoBackgroundEnabled) {
      void videoEl.play().catch(() => {});
    }
  }, [activeGameSession, publishedTheme.performanceFriendlyMode, publishedTheme.backgroundType, publishedTheme.videoBackgroundEnabled]);

  const games = catalog?.games ?? [];
  const categories = useMemo(
    () => [...(catalog?.categories ?? [])].sort((a, b) => a.displayOrder - b.displayOrder),
    [catalog],
  );

  const selectedCategoryObj = useMemo(
    () => categories.find((c) => c.id === selectedCategoryId) ?? null,
    [categories, selectedCategoryId],
  );

  const featuredGame = useMemo(
    () => games.find((g) => g.isFeatured) ?? games[0] ?? null,
    [games],
  );

  const getCategoryLabel = (cat: CategoryEntry) => (isRtl ? cat.nameAr : cat.nameEn);

  const getGameCategoryNames = (game: GameEntry): string => {
    const matched = categories
      .filter((c) => !c.isSystem && game.categoryIds?.includes(c.id))
      .map((c) => getCategoryLabel(c));
    return matched.length > 0 ? matched.join(' · ') : game.category;
  };

  const filteredGames = useMemo(() => {
    let list = [...games];

    if (navMode === 'recent') {
      const orderMap = new Map(recentPlays.map((r, idx) => [r.id, idx]));
      list = list
        .filter((g) => orderMap.has(g.id))
        .sort((a, b) => (orderMap.get(a.id) ?? 999) - (orderMap.get(b.id) ?? 999));
    } else if (navMode === 'category' && selectedCategoryObj) {
      if (selectedCategoryObj.filterType === 'OnlineGames') {
        list = list.filter((g) => g.networkMode === 'Online');
      } else if (selectedCategoryObj.filterType === 'OfflineGames') {
        list = list.filter((g) => g.networkMode === 'Offline');
      } else if (selectedCategoryObj.filterType === 'Custom') {
        list = list.filter((g) => g.categoryIds?.includes(selectedCategoryObj.id));
      }
    }

    const q = searchQuery.trim().toLowerCase();
    if (q) {
      list = list.filter(
        (g) =>
          g.name.toLowerCase().includes(q) ||
          getGameCategoryNames(g).toLowerCase().includes(q) ||
          g.developer.toLowerCase().includes(q),
      );
    }

    return list;
  }, [games, navMode, selectedCategoryObj, recentPlays, searchQuery, isRtl]);

  const buildCommandLine = (game: GameEntry): string => {
    if (game.launchType === 'Steam') {
      return `"C:\\Program Files (x86)\\Steam\\steam.exe" -applaunch ${game.externalAppId} ${game.launchArguments}`.trim();
    }
    if (game.launchType === 'RiotClient') {
      return `"${game.executablePath || 'C:\\Riot Games\\Riot Client\\RiotClientServices.exe'}" --launch-product=${game.externalAppId} --launch-patchline=live ${game.launchArguments}`.trim();
    }
    return `"${game.executablePath}" ${game.launchArguments}`.trim();
  };

  const handleLaunchGame = (game: GameEntry) => {
    if (sessionLockRef.current || activeGameSession) {
      setLaunchFeedback(t.duplicateBlocked);
      return;
    }

    if (game.launchType === 'DirectExecutable' && !game.executablePath) {
      setLaunchFeedback(`${game.name}: UNC path missing.`);
      return;
    }

    sessionLockRef.current = true;
    setLaunchFeedback(null);
    setRecentPlays((prev) => [{ id: game.id }, ...prev.filter((item) => item.id !== game.id)]);
    setActiveGameSession({
      game,
      commandLine: buildCommandLine(game),
    });
  };

  const handleCloseGameSession = () => {
    sessionLockRef.current = false;
    setActiveGameSession(null);
  };

  const storageNameFor = (storageId: string): string =>
    catalog?.storageLocations.find((s) => s.id === storageId)?.displayName ?? storageId;

  const appDisplayName = isRtl
    ? (publishedTheme.applicationNameAr || t.brand)
    : (publishedTheme.applicationNameEn || t.brand);

  const cafeDisplayName = isRtl
    ? (publishedTheme.cyberCafeNameAr || t.subtitle)
    : (publishedTheme.cyberCafeNameEn || t.subtitle);

  const welcomeTitle = isRtl
    ? (publishedTheme.welcomeTitleAr || t.sectionHome)
    : (publishedTheme.welcomeTitleEn || t.sectionHome);

  const sectionHeading =
    navMode === 'home'
      ? welcomeTitle
      : navMode === 'recent'
        ? t.sectionRecent
        : selectedCategoryObj
          ? getCategoryLabel(selectedCategoryObj)
          : welcomeTitle;

  const isLocked = Boolean(activeGameSession);

  const themeVars = {
    '--ch-accent-primary': publishedTheme.primaryAccentColor,
    '--ch-accent-secondary': publishedTheme.secondaryAccentColor,
    '--ch-main-bg': publishedTheme.mainBackgroundColor,
    '--ch-sidebar-bg': publishedTheme.sidebarBackgroundColor,
    '--ch-card-bg': publishedTheme.gameCardBackgroundColor,
    '--ch-text-primary': publishedTheme.primaryTextColor,
    '--ch-text-secondary': publishedTheme.secondaryTextColor,
    '--ch-btn-bg': publishedTheme.buttonBackgroundColor,
    '--ch-btn-text': publishedTheme.buttonTextColor,
    '--ch-hover-bg': publishedTheme.hoverStateColor,
    '--ch-border': publishedTheme.borderColor,
    '--ch-active-nav': publishedTheme.activeNavItemColor,
    '--ch-card-radius':
      publishedTheme.cardShape === 'Sharp'
        ? '2px'
        : `${Math.max(0, publishedTheme.cardBorderRadius)}px`,
  } as CSSProperties;

  const backgroundStyle: CSSProperties =
    publishedTheme.backgroundType === 'Solid'
      ? { backgroundColor: publishedTheme.solidBackgroundColor }
      : {
          backgroundImage: `linear-gradient(${publishedTheme.gradientAngleDegrees}deg, ${publishedTheme.gradientStartColor}, ${publishedTheme.gradientEndColor})`,
          backgroundColor: publishedTheme.mainBackgroundColor,
        };

  const sidebarOrderClass =
    publishedTheme.sidebarPosition === 'Right'
      ? isRtl
        ? 'order-first'
        : 'order-last'
      : publishedTheme.sidebarPosition === 'Left'
        ? isRtl
          ? 'order-last'
          : 'order-first'
        : 'order-first';

  const gridColsClass =
    publishedTheme.gridColumns >= 5
      ? 'grid-cols-2 md:grid-cols-3 xl:grid-cols-5'
      : publishedTheme.gridColumns === 3
        ? 'grid-cols-2 md:grid-cols-3'
        : publishedTheme.gridColumns === 2
          ? 'grid-cols-1 md:grid-cols-2'
          : 'grid-cols-2 md:grid-cols-3 xl:grid-cols-4';

  const playBtnStyle: CSSProperties =
    publishedTheme.playButtonStyle === 'Solid'
      ? {
          backgroundColor: publishedTheme.buttonBackgroundColor,
          color: publishedTheme.buttonTextColor,
        }
      : publishedTheme.playButtonStyle === 'Outline'
        ? {
            backgroundColor: 'transparent',
            borderColor: publishedTheme.primaryAccentColor,
            borderWidth: '1px',
            color: publishedTheme.primaryTextColor,
          }
        : {
            backgroundImage: `linear-gradient(90deg, ${publishedTheme.primaryAccentColor}, ${publishedTheme.secondaryAccentColor})`,
            color: publishedTheme.buttonTextColor,
          };

  const cardHoverClass =
    publishedTheme.hoverAnimation === 'None'
      ? ''
      : publishedTheme.hoverAnimation === 'Lift'
        ? 'hover:-translate-y-1'
        : publishedTheme.hoverAnimation === 'Glow'
          ? 'hover:shadow-lg hover:shadow-violet-500/25'
          : '';

  return (
    <div
      dir={isRtl ? 'rtl' : 'ltr'}
      style={{ ...themeVars, ...backgroundStyle }}
      className="relative flex min-h-screen text-[var(--ch-text-primary)] overflow-hidden"
    >
      {/* Dynamic Image / Video Background Layer */}
      {(publishedTheme.backgroundType === 'Image' || publishedTheme.backgroundType === 'Video') && (
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 overflow-hidden"
          style={{
            opacity: publishedTheme.backgroundOpacity,
            filter: `brightness(${publishedTheme.backgroundBrightness}) blur(${publishedTheme.blurIntensity}px)`,
          }}
        >
          {(publishedTheme.staticFallbackImagePath || publishedTheme.backgroundImageAssetPath) && (
            <img
              src={publishedTheme.backgroundImageAssetPath || publishedTheme.staticFallbackImagePath}
              alt=""
              className={`w-full h-full ${publishedTheme.imageScaling === 'Contain' ? 'object-contain' : 'object-cover'}`}
            />
          )}
          {publishedTheme.backgroundType === 'Video' &&
            publishedTheme.videoBackgroundEnabled &&
            publishedTheme.backgroundVideoAssetPath && (
              <video
                ref={videoRef}
                src={publishedTheme.backgroundVideoAssetPath}
                autoPlay
                loop
                muted
                playsInline
                className="w-full h-full object-cover"
              />
            )}
          <div
            className="absolute inset-0 bg-black"
            style={{ opacity: publishedTheme.overlayOpacity }}
          />
        </div>
      )}

      {/* Navigation Rail (Customer-Only: Zero Admin/Dev/Favorites) */}
      <aside
        style={{ width: `${Math.min(340, Math.max(200, publishedTheme.sidebarWidth))}px` }}
        className={`relative z-10 shrink-0 bg-[var(--ch-sidebar-bg)] border-e border-[var(--ch-border)] flex flex-col justify-between p-6 ${sidebarOrderClass}`}
      >
        <div className="min-h-0 flex flex-col flex-1">
          <div className="mb-7 flex items-center gap-3">
            {publishedTheme.logoAssetPath && publishedTheme.logoPosition === 'SidebarTop' && (
              <img
                src={publishedTheme.logoAssetPath}
                alt={appDisplayName}
                style={{ height: `${publishedTheme.logoSizePx}px`, width: `${publishedTheme.logoSizePx}px` }}
                className="rounded-lg object-contain shrink-0"
              />
            )}
            <div className="min-w-0">
              <div className="font-display text-2xl font-extrabold tracking-tight text-[var(--ch-text-primary)] truncate">
                {appDisplayName}
              </div>
              <p className="text-xs text-[var(--ch-accent-secondary)] mt-0.5 truncate">{cafeDisplayName}</p>
            </div>
          </div>

          <nav className="space-y-1 overflow-y-auto flex-1 pe-1" aria-label="Launcher navigation">
            <button
              type="button"
              onClick={() => setNavMode('home')}
              style={navMode === 'home' ? { backgroundColor: publishedTheme.activeNavItemColor, borderColor: publishedTheme.primaryAccentColor } : undefined}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap border ${
                navMode === 'home'
                  ? 'text-[var(--ch-text-primary)]'
                  : 'border-transparent text-[var(--ch-text-secondary)] hover:text-[var(--ch-text-primary)] hover:bg-[var(--ch-hover-bg)]'
              }`}
            >
              <Home className="w-4 h-4 shrink-0 text-[var(--ch-accent-secondary)]" />
              <span>{t.navHome}</span>
            </button>

            <button
              type="button"
              onClick={() => setNavMode('recent')}
              style={navMode === 'recent' ? { backgroundColor: publishedTheme.activeNavItemColor, borderColor: publishedTheme.primaryAccentColor } : undefined}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap border ${
                navMode === 'recent'
                  ? 'text-[var(--ch-text-primary)]'
                  : 'border-transparent text-[var(--ch-text-secondary)] hover:text-[var(--ch-text-primary)] hover:bg-[var(--ch-hover-bg)]'
              }`}
            >
              <Clock className="w-4 h-4 shrink-0 text-[var(--ch-accent-secondary)]" />
              <span>{t.navRecent}</span>
            </button>

            <div className="pt-5 pb-2 px-3 text-[11px] font-medium text-[var(--ch-text-secondary)]">
              {t.navCategories}
            </div>

            {categories.map((cat) => {
              const active = navMode === 'category' && selectedCategoryId === cat.id;
              return (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => {
                    setSelectedCategoryId(cat.id);
                    setNavMode('category');
                  }}
                  style={active ? { backgroundColor: publishedTheme.activeNavItemColor, borderColor: publishedTheme.primaryAccentColor } : undefined}
                  className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap border ${
                    active
                      ? 'text-[var(--ch-text-primary)]'
                      : 'border-transparent text-[var(--ch-text-secondary)] hover:text-[var(--ch-text-primary)] hover:bg-[var(--ch-hover-bg)]'
                  }`}
                >
                  <CategoryIcon iconKey={cat.iconKey} />
                  <span className="truncate">{getCategoryLabel(cat)}</span>
                </button>
              );
            })}
          </nav>
        </div>

        <div className="pt-5 mt-4 border-t border-[var(--ch-border)]">
          <p className="text-xs text-[var(--ch-text-secondary)] mb-3 tabular-nums">
            v{catalog?.catalogVersion ?? 3} · {t.readOnlyTag}
          </p>
          <button
            type="button"
            onClick={() => void loadCatalogAndTheme(false)}
            className="w-full flex items-center justify-center gap-2 px-3.5 py-2 rounded-lg text-xs font-medium bg-[var(--ch-card-bg)] hover:bg-[var(--ch-hover-bg)] text-[var(--ch-text-primary)] border border-[var(--ch-border)] transition-colors whitespace-nowrap"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>{t.syncCatalog}</span>
          </button>
        </div>
      </aside>

      {/* Main Customer Viewport */}
      <div
        className="relative z-10 flex-1 flex flex-col min-w-0 p-6 lg:p-8 overflow-y-auto"
        style={{ gap: `${Math.max(12, publishedTheme.mainContentSpacing / 2)}px` }}
      >
        {/* Top Bar: Branding/Heading + Search + Visible AR / EN Language Switcher */}
        <header
          className="flex flex-wrap items-center justify-between gap-4"
          style={{ marginBottom: `${Math.max(8, publishedTheme.headerSpacing / 3)}px` }}
        >
          <div className="flex items-center gap-3 min-w-0">
            {publishedTheme.logoAssetPath && publishedTheme.logoPosition !== 'SidebarTop' && (
              <img
                src={publishedTheme.logoAssetPath}
                alt={appDisplayName}
                style={{ height: `${publishedTheme.logoSizePx}px`, width: `${publishedTheme.logoSizePx}px` }}
                className="rounded-lg object-contain shrink-0"
              />
            )}
            <h1 className="font-display text-2xl font-bold text-[var(--ch-text-primary)] tracking-tight truncate">
              {sectionHeading}
            </h1>
          </div>

          <div className="flex items-center gap-3 w-full sm:w-auto">
            <div className="relative flex-1 sm:w-72">
              <Search className="w-4 h-4 text-[var(--ch-text-secondary)] absolute start-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
              <input
                type="search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t.searchPlaceholder}
                aria-label={t.searchPlaceholder}
                className="w-full bg-[var(--ch-card-bg)] border border-[var(--ch-border)] rounded-lg ps-10 pe-4 py-2 text-sm text-[var(--ch-text-primary)] placeholder:text-[var(--ch-text-secondary)] focus:outline-none focus:border-[var(--ch-accent-secondary)]"
              />
            </div>

            <button
              type="button"
              onClick={onToggleLanguage}
              className="inline-flex items-center gap-2 px-3.5 py-2 rounded-lg text-xs font-semibold bg-[var(--ch-card-bg)] hover:bg-[var(--ch-hover-bg)] text-[var(--ch-accent-secondary)] border border-[var(--ch-border)] transition-colors whitespace-nowrap shrink-0"
            >
              <Languages className="w-4 h-4" />
              <span>{t.switchLang}</span>
            </button>
          </div>
        </header>

        {/* Active Single-Game Lock Banner */}
        {activeGameSession && (
          <div className="px-4 py-3.5 rounded-xl bg-violet-950/60 border border-[var(--ch-accent-primary)] text-white flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <Lock className="w-5 h-5 text-[var(--ch-accent-secondary)] shrink-0" />
              <div>
                <div className="text-sm font-bold">
                  {t.gameInProgressTitle} — {t.activeGamePrefix} {activeGameSession.game.name}
                </div>
                <div className="text-xs text-slate-300">{t.sessionLockNotice}</div>
              </div>
            </div>
          </div>
        )}

        {offlineWarning && (
          <div className="px-4 py-3 rounded-lg bg-amber-950/40 border border-amber-500/40 text-amber-200 text-sm">
            {offlineWarning}
          </div>
        )}

        {launchFeedback && (
          <div className="px-4 py-3 rounded-lg bg-red-950/50 border border-red-500/40 text-red-200 text-sm flex items-center justify-between">
            <span>{launchFeedback}</span>
            <button
              type="button"
              onClick={() => setLaunchFeedback(null)}
              className="text-xs text-slate-300 hover:text-white ms-4"
            >
              {t.dismiss}
            </button>
          </div>
        )}

        {/* Featured Hero Spotlight (Home View) */}
        {navMode === 'home' && featuredGame && (
          <div className="relative h-72 lg:h-80 rounded-xl overflow-hidden border border-[var(--ch-border)] shrink-0 bg-[var(--ch-card-bg)]">
            <img
              src={featuredGame.heroImagePath || FALLBACK_HERO}
              alt={featuredGame.name}
              referrerPolicy="no-referrer"
              className="w-full h-full object-cover opacity-55"
            />
            <div
              className={`absolute inset-0 ${
                isRtl
                  ? 'bg-gradient-to-l from-[#08090D] via-[#08090D]/80 to-transparent'
                  : 'bg-gradient-to-r from-[#08090D] via-[#08090D]/80 to-transparent'
              }`}
            />
            <div className="absolute inset-0 flex flex-col justify-center p-8 max-w-2xl">
              <div className="text-xs font-medium text-[var(--ch-accent-secondary)] mb-2">
                {getGameCategoryNames(featuredGame)} · {featuredGame.networkMode === 'Online' ? t.online : t.offline}
              </div>
              <h2 className="font-display text-3xl lg:text-4xl font-extrabold text-white tracking-tight mb-3 text-balance">
                {featuredGame.name}
              </h2>
              <p className="text-sm text-slate-300 line-clamp-2 mb-6 leading-relaxed">
                {featuredGame.description}
              </p>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  disabled={isLocked}
                  onClick={() => handleLaunchGame(featuredGame)}
                  style={playBtnStyle}
                  className="inline-flex items-center gap-2 px-6 py-2.5 rounded-lg text-sm font-semibold hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed transition-opacity shadow-lg whitespace-nowrap"
                >
                  <Play className="w-4 h-4 fill-current" />
                  <span>{t.playNow}</span>
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedGame(featuredGame)}
                  className="px-4 py-2.5 rounded-lg text-sm font-medium text-[var(--ch-text-primary)] bg-[var(--ch-card-bg)] hover:bg-[var(--ch-hover-bg)] border border-[var(--ch-border)] transition-colors whitespace-nowrap"
                >
                  {t.details}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Optional Category Banner when browsing a custom category */}
        {navMode === 'category' && selectedCategoryObj?.bannerImagePath && (
          <div className="h-36 rounded-xl overflow-hidden border border-[var(--ch-border)] relative">
            <img
              src={selectedCategoryObj.bannerImagePath}
              alt={getCategoryLabel(selectedCategoryObj)}
              referrerPolicy="no-referrer"
              className="w-full h-full object-cover opacity-60"
            />
            <div className="absolute inset-0 bg-gradient-to-t from-[#08090D] via-transparent to-transparent" />
          </div>
        )}

        {/* Game Cover Artwork Grid */}
        {loading ? (
          <div className={`grid ${gridColsClass}`} style={{ gap: `${publishedTheme.cardSpacing}px` }}>
            {[1, 2, 3, 4].map((n) => (
              <div
                key={n}
                style={{ borderRadius: 'var(--ch-card-radius)' }}
                className="aspect-[3/4] bg-[var(--ch-card-bg)] animate-pulse border border-[var(--ch-border)]"
              />
            ))}
          </div>
        ) : filteredGames.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-center border border-[var(--ch-border)] rounded-xl bg-[var(--ch-sidebar-bg)]">
            <Gamepad2 className="w-10 h-10 text-[var(--ch-text-secondary)] mb-3" />
            <p className="text-base font-medium text-[var(--ch-text-primary)]">{t.noGamesMatch}</p>
            <p className="text-sm text-[var(--ch-text-secondary)] mt-1">{t.noGamesHint}</p>
          </div>
        ) : (
          <div className={`grid ${gridColsClass}`} style={{ gap: `${publishedTheme.cardSpacing}px` }}>
            {filteredGames.map((game) => {
              const isRecent = recentPlays.some((r) => r.id === game.id);
              return (
                <div
                  key={game.id}
                  onClick={() => setSelectedGame(game)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      setSelectedGame(game);
                    }
                  }}
                  style={{ borderRadius: 'var(--ch-card-radius)' }}
                  className={`group relative overflow-hidden bg-[var(--ch-card-bg)] border transition-all duration-200 cursor-pointer flex flex-col focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ch-accent-secondary)] ${
                    publishedTheme.borderEffect === 'None'
                      ? 'border-transparent'
                      : publishedTheme.borderEffect === 'NeonGlow'
                        ? 'border-[var(--ch-accent-primary)]/50 hover:border-[var(--ch-accent-secondary)]'
                        : 'border-[var(--ch-border)] hover:border-[var(--ch-accent-primary)]'
                  } ${cardHoverClass}`}
                >
                  <div className="relative aspect-[3/4] w-full overflow-hidden bg-[#171A26]">
                    <img
                      src={game.coverImagePath}
                      alt={game.name}
                      referrerPolicy="no-referrer"
                      className={`w-full h-full object-cover transition-transform duration-200 ${
                        publishedTheme.hoverAnimation === 'ScaleUp' ? 'group-hover:scale-105' : ''
                      }`}
                    />
                    <div className="absolute inset-0 bg-gradient-to-t from-[var(--ch-card-bg)] via-transparent to-transparent opacity-80" />
                  </div>

                  <div className="p-4 flex-1 flex flex-col justify-between">
                    <div>
                      <h3
                        style={{ fontSize: `${Math.min(22, Math.max(13, publishedTheme.titleFontSize))}px` }}
                        className="font-display font-semibold text-[var(--ch-text-primary)] truncate"
                      >
                        {game.name}
                      </h3>
                      <p className="text-xs text-[var(--ch-text-secondary)] mt-1 truncate">
                        {getGameCategoryNames(game)} · {game.networkMode === 'Online' ? t.online : t.offline}
                        {isRecent ? ` · ${t.playedAgo}` : ''}
                      </p>
                      {publishedTheme.showDescriptionOnCard && game.description && (
                        <p className="text-xs text-[var(--ch-text-secondary)] mt-2 line-clamp-2">
                          {game.description}
                        </p>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Game Details Slide-Over Panel (No Favorites Button) */}
      {selectedGame && (
        <div className="relative z-20 w-full sm:w-96 shrink-0 bg-[var(--ch-sidebar-bg)] border-s border-[var(--ch-border)] p-6 flex flex-col justify-between overflow-y-auto">
          <div>
            <div className="flex items-center justify-between mb-5">
              <span className="text-xs font-medium text-[var(--ch-accent-secondary)]">{t.gameDetails}</span>
              <button
                type="button"
                aria-label={t.close}
                onClick={() => setSelectedGame(null)}
                className="p-1.5 rounded-lg text-[var(--ch-text-secondary)] hover:text-[var(--ch-text-primary)] hover:bg-[var(--ch-hover-bg)]"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="aspect-[3/4] max-h-64 w-full rounded-xl overflow-hidden border border-[var(--ch-border)] mb-5 bg-[var(--ch-card-bg)]">
              <img
                src={selectedGame.coverImagePath}
                alt={selectedGame.name}
                referrerPolicy="no-referrer"
                className="w-full h-full object-cover"
              />
            </div>

            <h2 className="font-display text-2xl font-bold text-[var(--ch-text-primary)] mb-1">
              {selectedGame.name}
            </h2>
            <p className="text-xs text-[var(--ch-text-secondary)] mb-4">
              {getGameCategoryNames(selectedGame)} · {selectedGame.developer}
            </p>
            <p className="text-sm text-[var(--ch-text-secondary)] leading-relaxed mb-6">
              {selectedGame.description}
            </p>

            <dl className="space-y-2.5 text-xs border-t border-[var(--ch-border)] pt-4">
              <div className="flex justify-between gap-3">
                <dt className="text-[var(--ch-text-secondary)]">{t.connectionMode}</dt>
                <dd className="text-[var(--ch-accent-secondary)] font-medium">
                  {selectedGame.networkMode === 'Online' ? t.online : t.offline}
                </dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-[var(--ch-text-secondary)]">{t.categoriesLabel}</dt>
                <dd className="text-[var(--ch-text-primary)] font-medium text-end">
                  {getGameCategoryNames(selectedGame)}
                </dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-[var(--ch-text-secondary)]">{t.launchMode}</dt>
                <dd className="text-[var(--ch-text-primary)] font-medium">{selectedGame.launchType}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-[var(--ch-text-secondary)]">{t.storageVolume}</dt>
                <dd className="text-[var(--ch-text-primary)] font-medium text-end">
                  {storageNameFor(selectedGame.storageLocationId)}
                </dd>
              </div>
              <div>
                <dt className="text-[var(--ch-text-secondary)] mb-1">{t.networkUncPath}</dt>
                <dd dir="ltr" className="font-mono text-[11px] text-[var(--ch-accent-secondary)] bg-[var(--ch-card-bg)] p-2 rounded border border-[var(--ch-border)] break-all text-left">
                  {selectedGame.executablePath || `steam://rungameid/${selectedGame.externalAppId}`}
                </dd>
              </div>
            </dl>
          </div>

          <div className="pt-6 mt-6 border-t border-[var(--ch-border)]">
            <button
              type="button"
              disabled={isLocked}
              onClick={() => handleLaunchGame(selectedGame)}
              style={playBtnStyle}
              className="w-full inline-flex items-center justify-center gap-2 px-5 py-3 rounded-lg text-sm font-semibold hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed transition-opacity shadow-lg"
            >
              <Play className="w-4 h-4 fill-current" />
              <span>{t.playGame}</span>
            </button>
          </div>
        </div>
      )}

      {/* Active Game Session Lock Overlay */}
      {activeGameSession && (
        <div className="absolute inset-0 z-30 bg-[#08090D]/95 backdrop-blur-sm flex items-center justify-center p-6">
          <div className="max-w-lg w-full bg-[var(--ch-card-bg)] border border-[var(--ch-border)] rounded-2xl p-8 text-center">
            <div className="w-12 h-12 rounded-xl bg-violet-600/20 border border-[var(--ch-accent-primary)] flex items-center justify-center mx-auto mb-5">
              <Lock className="w-6 h-6 text-[var(--ch-accent-secondary)]" />
            </div>
            <p className="text-xs font-semibold text-[var(--ch-accent-secondary)] mb-1">{t.gameInProgressTitle}</p>
            <h3 className="font-display text-2xl font-bold text-[var(--ch-text-primary)] mb-2">
              {activeGameSession.game.name}
            </h3>
            <p className="text-sm text-[var(--ch-text-secondary)] mb-4">
              {t.sessionLockNotice}
            </p>
            <p className="text-xs text-[var(--ch-text-secondary)] mb-4">{t.childProcessTracking}</p>
            <div dir="ltr" className="text-left font-mono text-xs text-[var(--ch-accent-secondary)] bg-[#090A0F] border border-[var(--ch-border)] rounded-lg p-3.5 mb-6 break-all">
              {activeGameSession.commandLine}
            </div>
            <button
              type="button"
              onClick={handleCloseGameSession}
              style={playBtnStyle}
              className="px-6 py-2.5 rounded-lg text-sm font-semibold hover:opacity-90 transition-opacity"
            >
              {t.closeGameReturn}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
