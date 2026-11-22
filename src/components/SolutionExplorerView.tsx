import { useEffect, useState } from 'react';
import { Check, Copy, FileCode2, FolderTree, ShieldAlert } from 'lucide-react';
import type { SolutionFileItem } from '../types';

export default function SolutionExplorerView() {
  const [files, setFiles] = useState<SolutionFileItem[]>([]);
  const [selectedPath, setSelectedPath] = useState<string>('docs/TESTING_SINGLE_CLIENT.md');
  const [content, setContent] = useState<string>('');
  const [loadingFile, setLoadingFile] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    void (async () => {
      const resp = await fetch('/api/solution/tree');
      if (resp.ok) {
        const data = (await resp.json()) as { files: SolutionFileItem[] };
        setFiles(data.files);
      }
    })();
  }, []);

  useEffect(() => {
    if (!selectedPath) return;
    setLoadingFile(true);
    void (async () => {
      try {
        const resp = await fetch(`/api/solution/file?path=${encodeURIComponent(selectedPath)}`);
        if (resp.ok) {
          const data = (await resp.json()) as { content: string };
          setContent(data.content);
        }
      } finally {
        setLoadingFile(false);
      }
    })();
  }, [selectedPath]);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(content);
    setCopied(true);
    setTimeout(() => setCopied(false), 1800);
  };

  return (
    <div className="space-y-6">
      {/* Important Environment Notice */}
      <div className="bg-[#11131C] border border-cyan-500/35 rounded-xl p-5 flex items-start gap-3.5">
        <ShieldAlert className="w-5 h-5 text-cyan-400 shrink-0 mt-0.5" />
        <div className="text-sm text-slate-300 leading-relaxed">
          <p className="font-semibold text-white mb-1">
            Full C# / .NET 8 WPF Solution Generated in <code className="font-mono text-cyan-300">/dotnet</code>
          </p>
          <p>
            This browser container runs on Linux without the Windows .NET 8 Desktop SDK, so the WPF binaries cannot be compiled here. Inspect all solution projects, XAML views, PowerShell CCBoot deployment scripts, xUnit tests, and the single-client test plan below, then run <code className="font-mono text-cyan-300">dotnet build CyberHub.sln</code> on Windows.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* File Tree */}
        <div className="lg:col-span-4 bg-[#0F1118] border border-[#1E2233] rounded-xl p-4 max-h-[680px] overflow-y-auto">
          <div className="flex items-center gap-2 text-xs font-semibold text-slate-300 mb-3 px-2">
            <FolderTree className="w-4 h-4 text-violet-400" />
            <span>dotnet/ ({files.length} files)</span>
          </div>
          <div className="space-y-1">
            {files.map((item) => (
              <button
                key={item.path}
                type="button"
                onClick={() => setSelectedPath(item.path)}
                className={`w-full flex items-center justify-between gap-2 px-2.5 py-2 rounded-lg text-left text-xs transition-colors ${
                  selectedPath === item.path
                    ? 'bg-violet-600/20 text-white border border-violet-500/40'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-[#161926]'
                }`}
              >
                <span className="font-mono truncate">{item.path}</span>
                <span className="font-mono tabular-nums text-[11px] text-slate-500 shrink-0">
                  {(item.sizeBytes / 1024).toFixed(1)} KB
                </span>
              </button>
            ))}
          </div>
        </div>

        {/* Source Code Viewer */}
        <div className="lg:col-span-8 bg-[#0B0D13] border border-[#1E2233] rounded-xl overflow-hidden flex flex-col max-h-[680px]">
          <div className="flex items-center justify-between px-4 py-3 bg-[#111420] border-b border-[#1E2233]">
            <div className="flex items-center gap-2 min-w-0">
              <FileCode2 className="w-4 h-4 text-cyan-400 shrink-0" />
              <span className="font-mono text-xs text-slate-200 truncate">{selectedPath}</span>
            </div>
            <button
              type="button"
              onClick={() => void handleCopy()}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium bg-[#1A1E2E] hover:bg-[#23283D] text-slate-200 border border-[#2B314A] transition-colors shrink-0"
            >
              {copied ? <Check className="w-3.5 h-3.5 text-cyan-400" /> : <Copy className="w-3.5 h-3.5" />}
              <span>{copied ? 'Copied' : 'Copy'}</span>
            </button>
          </div>
          <pre className="p-5 text-xs font-mono text-slate-200 overflow-auto leading-relaxed flex-1">
            {loadingFile ? 'Loading source file...' : content}
          </pre>
        </div>
      </div>
    </div>
  );
}
