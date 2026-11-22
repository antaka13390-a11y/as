import { useCallback, useEffect, useState } from 'react';
import {
  Database,
  Eye,
  EyeOff,
  Lock,
  Plus,
  RefreshCw,
  Save,
  ShieldCheck,
  Trash2,
} from 'lucide-react';
import type { AdminAuditLog, AdminStatus, GameEntry, LaunchType, StorageLocation } from '../types';

const PRESET_CATEGORIES = [
  'Tactical FPS',
  'MOBA',
  'Battle Royale',
  'Action RPG',
  'Racing',
  'Sports',
  'Strategy',
];

const PRESET_COVERS = [
  { label: 'Valor Protocol Cover', path: '/src/assets/images/cover_valor_protocol_1795374591215.jpg' },
  { label: 'Rift Vanguard Cover', path: '/src/assets/images/cover_rift_vanguard_1795374591582.jpg' },
  { label: 'Eclipse Chronicles Cover', path: '/src/assets/images/cover_eclipse_chronicles_1795374591361.jpg' },
  { label: 'Apex Overdrive Cover', path: '/src/assets/images/cover_apex_overdrive_1795374590869.jpg' },
];

export default function AdminConsoleView() {
  const [status, setStatus] = useState<AdminStatus | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [passwordInput, setPasswordInput] = useState('');
  const [authError, setAuthError] = useState<string | null>(null);
  const [authLoading, setAuthLoading] = useState(false);

  const [activeTab, setActiveTab] = useState<'library' | 'logs'>('library');
  const [games, setGames] = useState<GameEntry[]>([]);
  const [storageLocations, setStorageLocations] = useState<StorageLocation[]>([]);
  const [logs, setLogs] = useState<AdminAuditLog[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  // Form fields
  const [name, setName] = useState('');
  const [category, setCategory] = useState('Tactical FPS');
  const [developer, setDeveloper] = useState('');
  const [description, setDescription] = useState('');
  const [coverImagePath, setCoverImagePath] = useState(PRESET_COVERS[0].path);
  const [storageLocationId, setStorageLocationId] = useState('ccboot-primary');
  const [launchType, setLaunchType] = useState<LaunchType>('DirectExecutable');
  const [executablePath, setExecutablePath] = useState('\\\\CCBOOT-SRV\\Games\\CCBoot\\');
  const [launchArguments, setLaunchArguments] = useState('');
  const [externalAppId, setExternalAppId] = useState('');
  const [processNameToMonitor, setProcessNameToMonitor] = useState('');
  const [isVisible, setIsVisible] = useState(true);
  const [isFeatured, setIsFeatured] = useState(false);

  const fetchStatus = useCallback(async () => {
    const resp = await fetch('/api/admin/status');
    if (resp.ok) {
      setStatus((await resp.json()) as AdminStatus);
    }
  }, []);

  const loadAdminData = useCallback(async (authToken: string) => {
    const headers = { Authorization: `Bearer ${authToken}` };
    const [gamesResp, logsResp] = await Promise.all([
      fetch('/api/admin/games', { headers }),
      fetch('/api/admin/logs', { headers }),
    ]);

    if (gamesResp.status === 401 || logsResp.status === 401) {
      setToken(null);
      return;
    }

    if (gamesResp.ok) {
      const data = (await gamesResp.json()) as { games: GameEntry[]; storageLocations: StorageLocation[] };
      setGames(data.games);
      setStorageLocations(data.storageLocations);
    }

    if (logsResp.ok) {
      setLogs((await logsResp.json()) as AdminAuditLog[]);
    }
  }, []);

  useEffect(() => {
    void fetchStatus();
  }, [fetchStatus]);

  useEffect(() => {
    if (token) {
      void loadAdminData(token);
    }
  }, [token, loadAdminData]);

  const populateForm = (game: GameEntry) => {
    setSelectedId(game.id);
    setPendingDeleteId(null);
    setName(game.name);
    setCategory(game.category);
    setDeveloper(game.developer);
    setDescription(game.description);
    setCoverImagePath(game.coverImagePath);
    setStorageLocationId(game.storageLocationId);
    setLaunchType(game.launchType);
    setExecutablePath(game.executablePath);
    setLaunchArguments(game.launchArguments);
    setExternalAppId(game.externalAppId);
    setProcessNameToMonitor(game.processNameToMonitor);
    setIsVisible(game.isVisible);
    setIsFeatured(game.isFeatured);
  };

  const resetForNewGame = () => {
    setSelectedId(null);
    setPendingDeleteId(null);
    setName('');
    setCategory('Tactical FPS');
    setDeveloper('');
    setDescription('');
    setCoverImagePath(PRESET_COVERS[0].path);
    setStorageLocationId(storageLocations[0]?.id ?? 'ccboot-primary');
    setLaunchType('DirectExecutable');
    setExecutablePath('\\\\CCBOOT-SRV\\Games\\CCBoot\\');
    setLaunchArguments('');
    setExternalAppId('');
    setProcessNameToMonitor('');
    setIsVisible(true);
    setIsFeatured(false);
    setBanner('Ready to add a new game entry.');
  };

  const handleAuthSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setAuthError(null);
    setAuthLoading(true);
    try {
      const endpoint = status?.isConfigured ? '/api/admin/login' : '/api/admin/setup';
      const resp = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: passwordInput }),
      });
      const body = (await resp.json()) as { accessToken?: string; error?: string };
      if (!resp.ok || !body.accessToken) {
        throw new Error(body.error ?? 'Authentication failed.');
      }
      setToken(body.accessToken);
      setPasswordInput('');
      await fetchStatus();
    } catch (err) {
      setAuthError(err instanceof Error ? err.message : 'Authentication failed.');
    } finally {
      setAuthLoading(false);
    }
  };

  const handleSaveGame = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token) return;
    setBanner(null);

    const payload = {
      name,
      category,
      developer,
      description,
      coverImagePath,
      storageLocationId,
      launchType,
      executablePath,
      launchArguments,
      externalAppId,
      processNameToMonitor,
      isVisible,
      isFeatured,
    };

    const url = selectedId ? `/api/admin/games/${selectedId}` : '/api/admin/games';
    const method = selectedId ? 'PUT' : 'POST';

    const resp = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(payload),
    });

    const data = (await resp.json()) as GameEntry & { error?: string };
    if (!resp.ok) {
      setBanner(`Validation error: ${data.error ?? 'Unable to save game.'}`);
      return;
    }

    setBanner(selectedId ? `Updated "${data.name}" and published read-only catalog.` : `Added "${data.name}" to central library.`);
    setSelectedId(data.id);
    await loadAdminData(token);
  };

  const handleToggleVisibility = async (game: GameEntry) => {
    if (!token) return;
    const resp = await fetch(`/api/admin/games/${game.id}/visibility?visible=${!game.isVisible}`, {
      method: 'PATCH',
      headers: { Authorization: `Bearer ${token}` },
    });
    if (resp.ok) {
      setBanner(`${game.name} is now ${!game.isVisible ? 'visible' : 'hidden'} on client stations.`);
      await loadAdminData(token);
      if (selectedId === game.id) {
        setIsVisible(!game.isVisible);
      }
    }
  };

  const handleRemoveRecordOnly = async (game: GameEntry) => {
    if (!token) return;
    const resp = await fetch(`/api/admin/games/${game.id}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${token}` },
    });
    if (resp.ok) {
      setBanner(`Removed "${game.name}" from database library only. UNC files on CCBoot/SMB share were NOT deleted.`);
      setPendingDeleteId(null);
      if (selectedId === game.id) {
        resetForNewGame();
      }
      await loadAdminData(token);
    }
  };

  const handleBackupDownload = async () => {
    if (!token) return;
    const resp = await fetch('/api/admin/backup', {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!resp.ok) return;
    const data = (await resp.json()) as { createdAtUtc: string; snapshot: unknown };
    const blob = new Blob([JSON.stringify(data.snapshot, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `cyberhub-backup-${data.createdAtUtc.slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
    setBanner(`Created database backup snapshot (${data.createdAtUtc}).`);
    await loadAdminData(token);
  };

  if (!token) {
    return (
      <div className="min-h-[640px] flex items-center justify-center bg-[#08090D] border border-[#1E2233] rounded-xl p-6">
        <form
          onSubmit={handleAuthSubmit}
          className="w-full max-w-md bg-[#10121A] border border-[#24293D] rounded-2xl p-8"
        >
          <div className="flex items-center gap-3 mb-5">
            <div className="w-10 h-10 rounded-lg bg-violet-600/20 border border-violet-500/40 flex items-center justify-center">
              <Lock className="w-5 h-5 text-cyan-400" />
            </div>
            <div>
              <h2 className="font-display text-xl font-bold text-white">
                {status?.isConfigured ? 'Admin Panel Sign-In' : 'First-Run Admin Setup'}
              </h2>
              <p className="text-xs text-slate-400">
                Workstation: {status?.detectedWorkstation ?? 'ADMIN-PC'} · {status?.detectedClientIp ?? '192.168.10.50'}
              </p>
            </div>
          </div>

          <p className="text-sm text-slate-300 mb-5 leading-relaxed">
            {status?.isConfigured
              ? 'Enter the shared administrator password to manage the central game library and activity logs.'
              : 'No administrator password has been claimed yet. Choose a password (8+ characters) to initialize server authentication.'}
          </p>

          <label className="block text-xs font-medium text-slate-300 mb-1.5" htmlFor="admin-password">
            Administrator Password
          </label>
          <input
            id="admin-password"
            type="password"
            required
            minLength={status?.isConfigured ? 1 : 8}
            value={passwordInput}
            onChange={(e) => setPasswordInput(e.target.value)}
            placeholder={status?.isConfigured ? 'Enter administrator password' : 'Create password (min 8 chars)'}
            className="w-full bg-[#151824] border border-[#282E44] rounded-lg px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:outline-none focus:border-cyan-500 mb-4"
          />

          {authError && (
            <div className="mb-4 px-3.5 py-2.5 rounded-lg bg-red-950/50 border border-red-500/40 text-xs text-red-200">
              {authError}
            </div>
          )}

          <button
            type="submit"
            disabled={authLoading}
            className="w-full py-2.5 px-4 rounded-lg text-sm font-semibold text-white bg-gradient-to-r from-violet-600 to-cyan-500 hover:opacity-90 transition-opacity disabled:opacity-50"
          >
            {authLoading
              ? 'Verifying...'
              : status?.isConfigured
                ? 'Authenticate Admin Session'
                : 'Claim Administrator Password'}
          </button>
        </form>
      </div>
    );
  }

  return (
    <div className="bg-[#0B0D14] border border-[#1E2233] rounded-xl p-6 space-y-6">
      {/* Console Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 pb-5 border-b border-[#1E2233]">
        <div>
          <div className="flex items-center gap-2 text-xs text-cyan-400 font-medium mb-1">
            <ShieldCheck className="w-4 h-4" />
            <span>Authenticated Admin Session · {status?.detectedWorkstation ?? 'ADMIN-PC'}</span>
          </div>
          <h1 className="font-display text-2xl font-bold text-white">
            Central Library Administration
          </h1>
        </div>

        <div className="flex flex-wrap items-center gap-2.5">
          <button
            type="button"
            onClick={resetForNewGame}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-violet-600 hover:bg-violet-500 text-white transition-colors whitespace-nowrap"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>New Game</span>
          </button>
          <button
            type="button"
            onClick={() => void handleBackupDownload()}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-medium bg-[#151824] hover:bg-[#1D2133] text-slate-200 border border-[#282E44] transition-colors whitespace-nowrap"
          >
            <Database className="w-3.5 h-3.5 text-cyan-400" />
            <span>Backup Library</span>
          </button>
          <button
            type="button"
            onClick={() => void loadAdminData(token)}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-medium bg-[#151824] hover:bg-[#1D2133] text-slate-200 border border-[#282E44] transition-colors whitespace-nowrap"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            <span>Refresh</span>
          </button>
          <button
            type="button"
            onClick={() => setToken(null)}
            className="px-3.5 py-2 rounded-lg text-xs font-medium text-slate-400 hover:text-white bg-[#12141E] border border-[#252A3D] transition-colors whitespace-nowrap"
          >
            Sign Out
          </button>
        </div>
      </div>

      {/* Sub-Tabs */}
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => setActiveTab('library')}
          className={`px-4 py-2 rounded-lg text-xs font-semibold transition-colors ${
            activeTab === 'library'
              ? 'bg-violet-600/20 text-white border border-violet-500/40'
              : 'text-slate-400 hover:text-white bg-[#12141E] border border-[#222638]'
          }`}
        >
          Game Library ({games.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('logs')}
          className={`px-4 py-2 rounded-lg text-xs font-semibold transition-colors ${
            activeTab === 'logs'
              ? 'bg-violet-600/20 text-white border border-violet-500/40'
              : 'text-slate-400 hover:text-white bg-[#12141E] border border-[#222638]'
          }`}
        >
          Activity Audit Logs ({logs.length})
        </button>
      </div>

      {banner && (
        <div className="px-4 py-3 rounded-lg bg-[#141827] border border-cyan-500/30 text-sm text-cyan-200">
          {banner}
        </div>
      )}

      {activeTab === 'library' ? (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
          {/* Left Library List */}
          <div className="lg:col-span-5 space-y-2.5">
            {games.map((game) => (
              <div
                key={game.id}
                onClick={() => populateForm(game)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    populateForm(game);
                  }
                }}
                className={`p-3.5 rounded-xl border transition-colors cursor-pointer flex items-center justify-between gap-3 ${
                  selectedId === game.id
                    ? 'bg-[#171B2B] border-violet-500/60'
                    : 'bg-[#10121A] border-[#1E2233] hover:border-[#2E344D]'
                }`}
              >
                <div className="flex items-center gap-3 min-w-0">
                  <img
                    src={game.coverImagePath}
                    alt={game.name}
                    referrerPolicy="no-referrer"
                    className="w-11 h-14 rounded-md object-cover shrink-0 bg-[#181B26]"
                  />
                  <div className="min-w-0">
                    <div className="font-medium text-sm text-white truncate">{game.name}</div>
                    <div className="text-xs text-slate-400 truncate mt-0.5">
                      {game.category} · {game.launchType} · {game.isVisible ? 'Visible' : 'Hidden'}
                    </div>
                  </div>
                </div>

                <button
                  type="button"
                  title={game.isVisible ? 'Hide from clients' : 'Show to clients'}
                  onClick={(e) => {
                    e.stopPropagation();
                    void handleToggleVisibility(game);
                  }}
                  className="p-2 rounded-lg text-slate-400 hover:text-white hover:bg-[#21263B] shrink-0"
                >
                  {game.isVisible ? <Eye className="w-4 h-4 text-cyan-400" /> : <EyeOff className="w-4 h-4" />}
                </button>
              </div>
            ))}
          </div>

          {/* Right Configuration Editor */}
          <form onSubmit={handleSaveGame} className="lg:col-span-7 bg-[#10121A] border border-[#1E2233] rounded-xl p-6 space-y-4">
            <h2 className="font-display text-lg font-bold text-white">
              {selectedId ? 'Edit Game Record' : 'Add Game to Central Library'}
            </h2>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Game Title</label>
                <input
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g., Counter-Strike 2"
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                />
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Category</label>
                <select
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                >
                  {PRESET_CATEGORIES.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                </select>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Developer / Studio</label>
                <input
                  type="text"
                  value={developer}
                  onChange={(e) => setDeveloper(e.target.value)}
                  placeholder="Studio name"
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                />
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Storage Location (UNC Share)</label>
                <select
                  value={storageLocationId}
                  onChange={(e) => setStorageLocationId(e.target.value)}
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                >
                  {storageLocations.map((loc) => (
                    <option key={loc.id} value={loc.id}>{loc.displayName}</option>
                  ))}
                </select>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Launch Type</label>
                <select
                  value={launchType}
                  onChange={(e) => setLaunchType(e.target.value as LaunchType)}
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                >
                  <option value="DirectExecutable">Direct Executable (.exe)</option>
                  <option value="Steam">Steam (AppID)</option>
                  <option value="RiotClient">Riot Client (Product ID)</option>
                  <option value="ExternalLauncher">External Launcher</option>
                </select>
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Steam AppID / Riot Product ID</label>
                <input
                  type="text"
                  value={externalAppId}
                  onChange={(e) => setExternalAppId(e.target.value)}
                  placeholder="e.g., 730 or valorant"
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm font-mono text-white focus:outline-none focus:border-cyan-500"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs text-slate-400 mb-1">UNC Executable Path</label>
              <input
                type="text"
                value={executablePath}
                onChange={(e) => setExecutablePath(e.target.value)}
                placeholder="\\CCBOOT-SRV\Games\CCBoot\GameFolder\Game.exe"
                className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm font-mono text-cyan-300 focus:outline-none focus:border-cyan-500"
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Launch Arguments</label>
                <input
                  type="text"
                  value={launchArguments}
                  onChange={(e) => setLaunchArguments(e.target.value)}
                  placeholder="-novid -high"
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm font-mono text-white focus:outline-none focus:border-cyan-500"
                />
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Process to Monitor</label>
                <input
                  type="text"
                  value={processNameToMonitor}
                  onChange={(e) => setProcessNameToMonitor(e.target.value)}
                  placeholder="e.g., cs2"
                  className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm font-mono text-white focus:outline-none focus:border-cyan-500"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs text-slate-400 mb-1">Cover Artwork Preset</label>
              <select
                value={coverImagePath}
                onChange={(e) => setCoverImagePath(e.target.value)}
                className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
              >
                {PRESET_COVERS.map((c) => (
                  <option key={c.path} value={c.path}>{c.label}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs text-slate-400 mb-1">Description</label>
              <textarea
                rows={2}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="w-full bg-[#151824] border border-[#262B3E] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
              />
            </div>

            <div className="flex flex-wrap items-center gap-6 pt-1">
              <label className="inline-flex items-center gap-2 text-sm text-slate-200 cursor-pointer">
                <input
                  type="checkbox"
                  checked={isVisible}
                  onChange={(e) => setIsVisible(e.target.checked)}
                  className="rounded border-slate-600 bg-[#151824] text-violet-600"
                />
                <span>Visible in Client Catalog</span>
              </label>

              <label className="inline-flex items-center gap-2 text-sm text-slate-200 cursor-pointer">
                <input
                  type="checkbox"
                  checked={isFeatured}
                  onChange={(e) => setIsFeatured(e.target.checked)}
                  className="rounded border-slate-600 bg-[#151824] text-violet-600"
                />
                <span>Featured Hero Banner</span>
              </label>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3 pt-4 border-t border-[#1E2233]">
              <button
                type="submit"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-semibold text-white bg-gradient-to-r from-violet-600 to-cyan-500 hover:opacity-90 transition-opacity"
              >
                <Save className="w-4 h-4" />
                <span>{selectedId ? 'Save Changes' : 'Add Game Record'}</span>
              </button>

              {selectedId && (
                pendingDeleteId === selectedId ? (
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-amber-300">DB record only (files stay safe):</span>
                    <button
                      type="button"
                      onClick={() => {
                        const target = games.find((g) => g.id === selectedId);
                        if (target) void handleRemoveRecordOnly(target);
                      }}
                      className="px-3 py-2 rounded-lg text-xs font-semibold bg-red-700 hover:bg-red-600 text-white"
                    >
                      Confirm Remove
                    </button>
                    <button
                      type="button"
                      onClick={() => setPendingDeleteId(null)}
                      className="px-3 py-2 rounded-lg text-xs text-slate-300 bg-[#171A26]"
                    >
                      Cancel
                    </button>
                  </div>
                ) : (
                  <button
                    type="button"
                    onClick={() => setPendingDeleteId(selectedId)}
                    className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium bg-red-950/60 hover:bg-red-900/70 text-red-200 border border-red-500/30 transition-colors"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    <span>Remove Library Record</span>
                  </button>
                )
              )}
            </div>
          </form>
        </div>
      ) : (
        <div className="bg-[#10121A] border border-[#1E2233] rounded-xl overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-[#1E2233] text-xs font-medium text-slate-400 bg-[#131622]">
                  <th className="py-3 px-4">Timestamp (UTC)</th>
                  <th className="py-3 px-4">Action</th>
                  <th className="py-3 px-4">Target Game</th>
                  <th className="py-3 px-4">Workstation</th>
                  <th className="py-3 px-4">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[#1A1D2B] text-xs">
                {logs.map((entry) => (
                  <tr key={entry.id} className="hover:bg-[#141724]">
                    <td className="py-2.5 px-4 font-mono tabular-nums text-slate-400 whitespace-nowrap">
                      {entry.timestampUtc.replace('T', ' ').slice(0, 19)}
                    </td>
                    <td className="py-2.5 px-4 font-mono text-cyan-300 whitespace-nowrap">
                      {entry.action}
                    </td>
                    <td className="py-2.5 px-4 text-slate-200">
                      {entry.targetGameName || '—'}
                    </td>
                    <td className="py-2.5 px-4 font-mono text-slate-300 whitespace-nowrap">
                      {entry.workstationName} ({entry.sourceIp})
                    </td>
                    <td className="py-2.5 px-4 text-slate-300">{entry.details}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
