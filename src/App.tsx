import { useState } from 'react';
import { Maximize2, Minimize2 } from 'lucide-react';
import AdminConsoleView from './components/AdminConsoleView';
import ClientLauncherView from './components/ClientLauncherView';
import SolutionExplorerView from './components/SolutionExplorerView';

type AppWorkspace = 'client' | 'admin' | 'solution';

export default function App() {
  const [workspace, setWorkspace] = useState<AppWorkspace>('client');
  const [isFullscreenPreview, setIsFullscreenPreview] = useState(false);

  return (
    <div className="min-h-screen bg-[#08090D] text-slate-100 flex flex-col">
      {/* Three-Zone Top Bar Contract */}
      <header className="border-b border-[#1E2233] bg-[#0B0D14]">
        <div className="max-w-[1440px] mx-auto px-6 h-16 flex items-center justify-between gap-8">
          {/* Zone 1: Single-Line Brand Wordmark */}
          <span className="font-display text-lg font-extrabold tracking-tight text-white whitespace-nowrap shrink-0">
            CYBER HUB
          </span>

          {/* Zone 2: Concise Single-Line Navigation Links */}
          <nav className="flex items-center gap-6 text-sm font-medium" aria-label="Workspace switcher">
            <button
              type="button"
              onClick={() => setWorkspace('client')}
              className={`py-1 transition-colors whitespace-nowrap shrink-0 border-b-2 ${
                workspace === 'client'
                  ? 'text-white border-cyan-400'
                  : 'text-slate-400 hover:text-white border-transparent'
              }`}
            >
              Client Launcher
            </button>
            <button
              type="button"
              onClick={() => {
                setIsFullscreenPreview(false);
                setWorkspace('admin');
              }}
              className={`py-1 transition-colors whitespace-nowrap shrink-0 border-b-2 ${
                workspace === 'admin'
                  ? 'text-white border-violet-500'
                  : 'text-slate-400 hover:text-white border-transparent'
              }`}
            >
              Admin Console
            </button>
            <button
              type="button"
              onClick={() => {
                setIsFullscreenPreview(false);
                setWorkspace('solution');
              }}
              className={`py-1 transition-colors whitespace-nowrap shrink-0 border-b-2 ${
                workspace === 'solution'
                  ? 'text-white border-cyan-400'
                  : 'text-slate-400 hover:text-white border-transparent'
              }`}
            >
              .NET 8 Solution
            </button>
          </nav>

          {/* Zone 3: Single Primary Action */}
          <div className="shrink-0">
            {workspace === 'client' ? (
              <button
                type="button"
                onClick={() => setIsFullscreenPreview((v) => !v)}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold text-white bg-gradient-to-r from-violet-600 to-cyan-500 hover:opacity-90 transition-opacity whitespace-nowrap shrink-0"
              >
                {isFullscreenPreview ? (
                  <>
                    <Minimize2 className="w-3.5 h-3.5" />
                    <span>Exit Fullscreen</span>
                  </>
                ) : (
                  <>
                    <Maximize2 className="w-3.5 h-3.5" />
                    <span>Fullscreen Kiosk</span>
                  </>
                )}
              </button>
            ) : (
              <button
                type="button"
                onClick={() => setWorkspace('client')}
                className="px-4 py-2 rounded-lg text-xs font-semibold text-white bg-[#171A26] hover:bg-[#222638] border border-[#2A3045] transition-colors whitespace-nowrap shrink-0"
              >
                Preview Client
              </button>
            )}
          </div>
        </div>
      </header>

      <main
        className={
          isFullscreenPreview && workspace === 'client'
            ? 'flex-1 w-full'
            : 'flex-1 max-w-[1440px] w-full mx-auto px-6 py-6'
        }
      >
        {workspace === 'client' && <ClientLauncherView isFullscreenPreview={isFullscreenPreview} />}
        {workspace === 'admin' && <AdminConsoleView />}
        {workspace === 'solution' && <SolutionExplorerView />}
      </main>
    </div>
  );
}
