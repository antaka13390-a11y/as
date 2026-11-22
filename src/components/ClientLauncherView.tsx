import { useEffect, useMemo, useState } from 'react';
import {
  Clock,
  Gamepad2,
  Heart,
  Home,
  LayoutGrid,
  Play,
  RefreshCw,
  Search,
  Star,
  X,
} from 'lucide-react';
import type { CatalogSnapshot, GameEntry } from '../types';

type LauncherSection = 'home' | 'all' | 'favorites' | 'recent';

interface ClientLauncherViewProps {
  isFullscreenPreview: boolean;
}

const FALLBACK_HERO = '/src/assets/images/hero_cyber_arena_1795374591030.jpg';

export default function ClientLauncherView({ isFullscreenPreview }: ClientLauncherViewProps) {
  const [catalog, setCatalog] = useState<CatalogSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [offlineWarning, setOfflineWarning] = useState<string | null>(null);
  const [section, setSection] = useState<LauncherSection>('home');
  const [categoryFilter, setCategoryFilter] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedGame, setSelectedGame] = useState<GameEntry | null>(null);
  const [favoriteIds, setFavoriteIds] = useState<string[]>([
    '11111111-1111-4444-8888-100000000001',
    '11111111-1111-4444-8888-100000000003',
  ]);
  const [recentPlays, setRecentPlays] = useState<Array<{ id: string; playedAt: string }>>([
    { id: '11111111-1111-4444-8888-100000000001', playedAt: '12 min ago' },
  ]);
  const [activeGameSession, setActiveGameSession] = useState<{
    game: GameEntry;
    commandLine: string;
  } | null>(null);
  const [launchFeedback, setLaunchFeedback] = useState<string | null>(null);

  const loadCatalog = async () => {
    setLoading(true);
    setOfflineWarning(null);
    try {
      const resp = await fetch('/api/catalog');
      if (!resp.ok) {
        throw new Error(`HTTP ${resp.status}`);
      }
      const data = (await resp.json()) as CatalogSnapshot;
      setCatalog(data);
    } catch {
      setOfflineWarning('Central ServerService unreachable. Displaying last synchronized station catalog.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadCatalog();
  }, []);

  const games = catalog?.games ?? [];

  const categories = useMemo(() => {
    const unique = Array.from(new Set(games.map((g) => g.category))).sort();
    return ['All', ...unique];
  }, [games]);

  const featuredGame = useMemo(() => {
    return games.find((g) => g.isFeatured) ?? games[0] ?? null;
  }, [games]);

  const filteredGames = useMemo(() => {
    let list = [...games];
    if (section === 'favorites') {
      list = list.filter((g) => favoriteIds.includes(g.id));
    } else if (section === 'recent') {
      const orderMap = new Map(recentPlays.map((r, idx) => [r.id, idx]));
      list = list
        .filter((g) => orderMap.has(g.id))
        .sort((a, b) => (orderMap.get(a.id) ?? 999) - (orderMap.get(b.id) ?? 999));
    }

    if (categoryFilter !== 'All') {
      list = list.filter((g) => g.category === categoryFilter);
    }

    const q = searchQuery.trim().toLowerCase();
    if (q) {
      list = list.filter(
        (g) =>
          g.name.toLowerCase().includes(q) ||
          g.category.toLowerCase().includes(q) ||
          g.developer.toLowerCase().includes(q)
      );
    }

    return list;
  }, [games, section, favoriteIds, recentPlays, categoryFilter, searchQuery]);

  const toggleFavorite = (id: string) => {
    setFavoriteIds((prev) => (prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]));
  };

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
    if (activeGameSession) {
      setLaunchFeedback(`Duplicate launch blocked: ${activeGameSession.game.name} is currently running.`);
      return;
    }

    if (game.launchType === 'DirectExecutable' && !game.executablePath) {
      setLaunchFeedback(`Cannot launch ${game.name}: UNC executable path is missing.`);
      return;
    }

    setLaunchFeedback(null);
    setRecentPlays((prev) => [
      { id: game.id, playedAt: 'Just now' },
      ...prev.filter((item) => item.id !== game.id),
    ]);
    setActiveGameSession({
      game,
      commandLine: buildCommandLine(game),
    });
  };

  const storageNameFor = (storageId: string): string => {
    return catalog?.storageLocations.find((s) => s.id === storageId)?.displayName ?? storageId;
  };

  const sectionHeading = {
    home: 'Featured & Quick Launch',
    all: 'All Games Library',
    favorites: 'Station Favorites',
    recent: 'Recently Played',
  }[section];

  return (
    <div
      className={`relative flex bg-[#08090D] text-slate-100 border border-[#1E2233] overflow-hidden ${
        isFullscreenPreview ? 'min-h-[calc(100vh-4rem)] rounded-none border-x-0 border-b-0' : 'min-h-[760px] rounded-xl'
      }`}
    >
      {/* Left Navigation Rail (Customer Interface Only — Zero Admin Controls) */}
      <aside className="w-60 shrink-0 bg-[#0F1118] border-r border-[#1E2233] flex flex-col justify-between p-6">
        <div>
          <div className="mb-8">
            <div className="font-display text-xl font-bold tracking-tight text-white whitespace-nowrap">
              CYBER HUB
            </div>
            <p className="text-xs text-cyan-400 mt-1">Station Launcher</p>
          </div>

          <nav className="space-y-1.5" aria-label="Launcher navigation">
            <button
              type="button"
              onClick={() => setSection('home')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap ${
                section === 'home'
                  ? 'bg-violet-600/20 text-white border border-violet-500/40'
                  : 'text-slate-400 hover:text-white hover:bg-[#171A26]'
              }`}
            >
              <Home className="w-4 h-4 shrink-0 text-cyan-400" />
              <span>Home</span>
            </button>

            <button
              type="button"
              onClick={() => setSection('all')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap ${
                section === 'all'
                  ? 'bg-violet-600/20 text-white border border-violet-500/40'
                  : 'text-slate-400 hover:text-white hover:bg-[#171A26]'
              }`}
            >
              <LayoutGrid className="w-4 h-4 shrink-0 text-cyan-400" />
              <span>All Games</span>
            </button>

            <button
              type="button"
              onClick={() => setSection('favorites')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap ${
                section === 'favorites'
                  ? 'bg-violet-600/20 text-white border border-violet-500/40'
                  : 'text-slate-400 hover:text-white hover:bg-[#171A26]'
              }`}
            >
              <Heart className="w-4 h-4 shrink-0 text-cyan-400" />
              <span>Favorites</span>
            </button>

            <button
              type="button"
              onClick={() => setSection('recent')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-lg text-sm font-medium transition-colors whitespace-nowrap ${
                section === 'recent'
                  ? 'bg-violet-600/20 text-white border border-violet-500/40'
                  : 'text-slate-400 hover:text-white hover:bg-[#171A26]'
              }`}
            >
              <Clock className="w-4 h-4 shrink-0 text-cyan-400" />
              <span>Recently Played</span>
            </button>
          </nav>
        </div>

        <div className="pt-6 border-t border-[#1E2233]">
          <p className="text-xs text-slate-400 mb-3">
            Catalog v{catalog?.catalogVersion ?? 1} · Read-Only
          </p>
          <button
            type="button"
            onClick={() => void loadCatalog()}
            className="w-full flex items-center justify-center gap-2 px-3.5 py-2 rounded-lg text-xs font-medium bg-[#171A26] hover:bg-[#1F2435] text-slate-200 border border-[#272C40] transition-colors whitespace-nowrap"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>Sync Catalog</span>
          </button>
        </div>
      </aside>

      {/* Main Customer Viewport */}
      <div className="flex-1 flex flex-col min-w-0 p-6 lg:p-8 overflow-y-auto">
        {/* Top Search Row */}
        <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
          <h1 className="font-display text-2xl font-bold text-white tracking-tight">
            {sectionHeading}
          </h1>

          <div className="relative w-full sm:w-80">
            <Search className="w-4 h-4 text-slate-400 absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="search"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search games, genres, studios..."
              aria-label="Search games"
              className="w-full bg-[#12141E] border border-[#252A3D] rounded-lg pl-10 pr-4 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:outline-none focus:border-cyan-500"
            />
          </div>
        </div>

        {offlineWarning && (
          <div className="mb-5 px-4 py-3 rounded-lg bg-amber-950/40 border border-amber-500/40 text-amber-200 text-sm">
            {offlineWarning}
          </div>
        )}

        {launchFeedback && (
          <div className="mb-5 px-4 py-3 rounded-lg bg-violet-950/50 border border-violet-500/40 text-violet-100 text-sm flex items-center justify-between">
            <span>{launchFeedback}</span>
            <button
              type="button"
              onClick={() => setLaunchFeedback(null)}
              className="text-xs text-slate-300 hover:text-white ml-4"
            >
              Dismiss
            </button>
          </div>
        )}

        {/* Featured Hero Spotlight (Home View) */}
        {section === 'home' && featuredGame && (
          <div className="relative h-72 lg:h-80 rounded-xl overflow-hidden border border-[#252A3D] mb-7 shrink-0 bg-[#10121B]">
            <img
              src={featuredGame.heroImagePath || FALLBACK_HERO}
              alt={featuredGame.name}
              referrerPolicy="no-referrer"
              className="w-full h-full object-cover opacity-55"
            />
            <div className="absolute inset-0 bg-gradient-to-r from-[#08090D] via-[#08090D]/80 to-transparent" />
            <div className="absolute inset-0 flex flex-col justify-center p-8 max-w-2xl">
              <div className="text-xs font-medium text-cyan-400 mb-2">
                {featuredGame.category} · {featuredGame.developer}
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
                  onClick={() => handleLaunchGame(featuredGame)}
                  className="inline-flex items-center gap-2 px-6 py-2.5 rounded-lg text-sm font-semibold text-white bg-gradient-to-r from-violet-600 to-cyan-500 hover:opacity-90 transition-opacity shadow-lg shadow-violet-600/25 whitespace-nowrap"
                >
                  <Play className="w-4 h-4 fill-current" />
                  <span>Play Now</span>
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedGame(featuredGame)}
                  className="px-4 py-2.5 rounded-lg text-sm font-medium text-slate-200 bg-[#171A26]/90 hover:bg-[#212536] border border-[#2D3348] transition-colors whitespace-nowrap"
                >
                  Game Details
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Category Filter Tabs */}
        <div className="flex items-center gap-2 overflow-x-auto pb-2 mb-6">
          {categories.map((cat) => (
            <button
              key={cat}
              type="button"
              onClick={() => setCategoryFilter(cat)}
              className={`px-3.5 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap shrink-0 ${
                categoryFilter === cat
                  ? 'bg-violet-600 text-white'
                  : 'bg-[#12141E] text-slate-400 hover:text-white border border-[#252A3D]'
              }`}
            >
              {cat}
            </button>
          ))}
        </div>

        {/* Game Cover Artwork Grid */}
        {loading ? (
          <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-5">
            {[1, 2, 3, 4].map((n) => (
              <div key={n} className="aspect-[3/4] rounded-xl bg-[#12141E] animate-pulse border border-[#1E2233]" />
            ))}
          </div>
        ) : filteredGames.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-center border border-[#1E2233] rounded-xl bg-[#0F1118]">
            <Gamepad2 className="w-10 h-10 text-slate-500 mb-3" />
            <p className="text-base font-medium text-slate-200">No games match this filter</p>
            <p className="text-sm text-slate-400 mt-1">
              Clear your search query or switch back to All Games.
            </p>
          </div>
        ) : (
          <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-5">
            {filteredGames.map((game) => {
              const isFav = favoriteIds.includes(game.id);
              const recentInfo = recentPlays.find((r) => r.id === game.id);
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
                  className="group relative rounded-xl overflow-hidden bg-[#11131C] border border-[#222638] hover:border-violet-500/60 transition-all duration-200 cursor-pointer flex flex-col focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
                >
                  <div className="relative aspect-[3/4] w-full overflow-hidden bg-[#171A26]">
                    <img
                      src={game.coverImagePath}
                      alt={game.name}
                      referrerPolicy="no-referrer"
                      className="w-full h-full object-cover transition-transform duration-200 group-hover:scale-105"
                    />
                    <div className="absolute inset-0 bg-gradient-to-t from-[#11131C] via-transparent to-transparent opacity-80" />
                    <button
                      type="button"
                      aria-label={isFav ? `Remove ${game.name} from favorites` : `Add ${game.name} to favorites`}
                      onClick={(e) => {
                        e.stopPropagation();
                        toggleFavorite(game.id);
                      }}
                      className="absolute top-3 right-3 p-2 rounded-lg bg-[#08090D]/75 border border-white/10 text-slate-200 hover:text-cyan-400 transition-colors"
                    >
                      <Star className={`w-4 h-4 ${isFav ? 'fill-cyan-400 text-cyan-400' : ''}`} />
                    </button>
                  </div>

                  <div className="p-4 flex-1 flex flex-col justify-between">
                    <div>
                      <h3 className="font-display text-base font-semibold text-white truncate">
                        {game.name}
                      </h3>
                      <p className="text-xs text-slate-400 mt-1 truncate">
                        {game.category}
                        {recentInfo ? ` · Played ${recentInfo.playedAt}` : ` · ${game.developer}`}
                      </p>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Game Details Slide-Over Panel */}
      {selectedGame && (
        <div className="w-full sm:w-96 shrink-0 bg-[#0F1118] border-l border-[#222638] p-6 flex flex-col justify-between overflow-y-auto">
          <div>
            <div className="flex items-center justify-between mb-5">
              <span className="text-xs font-medium text-cyan-400">Game Details</span>
              <button
                type="button"
                aria-label="Close game details"
                onClick={() => setSelectedGame(null)}
                className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-[#1A1D2B]"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="aspect-[3/4] max-h-64 w-full rounded-xl overflow-hidden border border-[#252A3D] mb-5 bg-[#171A26]">
              <img
                src={selectedGame.coverImagePath}
                alt={selectedGame.name}
                referrerPolicy="no-referrer"
                className="w-full h-full object-cover"
              />
            </div>

            <h2 className="font-display text-2xl font-bold text-white mb-1">
              {selectedGame.name}
            </h2>
            <p className="text-xs text-slate-400 mb-4">
              {selectedGame.category} · {selectedGame.developer}
            </p>
            <p className="text-sm text-slate-300 leading-relaxed mb-6">
              {selectedGame.description}
            </p>

            <dl className="space-y-2.5 text-xs border-t border-[#1E2233] pt-4">
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Launch Mode</dt>
                <dd className="text-slate-200 font-medium">{selectedGame.launchType}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Storage Volume</dt>
                <dd className="text-slate-200 font-medium text-right">
                  {storageNameFor(selectedGame.storageLocationId)}
                </dd>
              </div>
              <div>
                <dt className="text-slate-400 mb-1">Network UNC Target</dt>
                <dd className="font-mono text-[11px] text-cyan-300 bg-[#141724] p-2 rounded border border-[#222638] break-all">
                  {selectedGame.executablePath || `steam://rungameid/${selectedGame.externalAppId}`}
                </dd>
              </div>
            </dl>
          </div>

          <div className="pt-6 mt-6 border-t border-[#1E2233] space-y-2.5">
            <button
              type="button"
              onClick={() => handleLaunchGame(selectedGame)}
              className="w-full inline-flex items-center justify-center gap-2 px-5 py-3 rounded-lg text-sm font-semibold text-white bg-gradient-to-r from-violet-600 to-cyan-500 hover:opacity-90 transition-opacity shadow-lg shadow-violet-600/20"
            >
              <Play className="w-4 h-4 fill-current" />
              <span>Play Game</span>
            </button>
            <button
              type="button"
              onClick={() => toggleFavorite(selectedGame.id)}
              className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg text-sm font-medium bg-[#171A26] hover:bg-[#202436] text-slate-200 border border-[#262B3E] transition-colors"
            >
              <Star
                className={`w-4 h-4 ${
                  favoriteIds.includes(selectedGame.id) ? 'fill-cyan-400 text-cyan-400' : ''
                }`}
              />
              <span>
                {favoriteIds.includes(selectedGame.id) ? 'Remove from Favorites' : 'Add to Favorites'}
              </span>
            </button>
          </div>
        </div>
      )}

      {/* Active Game Process Monitoring Overlay */}
      {activeGameSession && (
        <div className="absolute inset-0 z-30 bg-[#08090D]/95 backdrop-blur-sm flex items-center justify-center p-6">
          <div className="max-w-lg w-full bg-[#11131C] border border-[#2A2F45] rounded-2xl p-8 text-center">
            <div className="w-12 h-12 rounded-xl bg-violet-600/20 border border-violet-500/40 flex items-center justify-center mx-auto mb-5">
              <Gamepad2 className="w-6 h-6 text-cyan-400" />
            </div>
            <h3 className="font-display text-2xl font-bold text-white mb-2">
              {activeGameSession.game.name} Running
            </h3>
            <p className="text-sm text-slate-300 mb-5">
              ClientLauncher minimized and locked against duplicate launches. Waiting for game process exit to restore fullscreen launcher.
            </p>
            <div className="text-left font-mono text-xs text-cyan-300 bg-[#090A0F] border border-[#1E2233] rounded-lg p-3.5 mb-6 break-all">
              {activeGameSession.commandLine}
            </div>
            <button
              type="button"
              onClick={() => setActiveGameSession(null)}
              className="px-6 py-2.5 rounded-lg text-sm font-semibold bg-gradient-to-r from-violet-600 to-cyan-500 text-white hover:opacity-90 transition-opacity"
            >
              Close Game &amp; Return to Launcher
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
