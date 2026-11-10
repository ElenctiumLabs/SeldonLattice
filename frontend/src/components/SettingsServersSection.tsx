import { useServerStatus } from '../hooks/useServerStatus';
import { SectionCard } from './SettingsControls';

export function ServersSection({ disabled }: { disabled: boolean }) {
  const { status, loading, launching, stopping, launchingKey, stoppingKey, launch, stop, launchOne, stopOne, engineEnabled } = useServerStatus();

  const servers = [
    { key: 'chat', label: 'llama.cpp Chat', port: 9931, running: status?.chat.running ?? false, starting: status?.chat.starting ?? false },
    { key: 'embeddings', label: 'llama.cpp Embeddings', port: 8081, running: status?.embeddings.running ?? false, starting: status?.embeddings.starting ?? false },
  ];

  return (
    <SectionCard title="llama.cpp Servers" subtitle="Launch and monitor local chat and embedding inference">
      {/* Status banner */}
      {!loading && status && !status.allRunning && (
        <div
          className="flex items-center gap-2 p-3 rounded-lg border"
          style={{
            borderColor: servers.some((server) => server.starting) ? '#f59e0b55' : '#b05a3a55',
            background: servers.some((server) => server.starting) ? '#f59e0b12' : '#3a1d1d22',
            marginBottom: 4,
          }}
        >
          <span style={{ color: servers.some((server) => server.starting) ? '#f59e0b' : '#b78383', fontSize: 14 }}>⚠</span>
          <p style={{
            fontFamily: 'var(--pr-font-mono)', fontSize: 11,
            color: servers.some((server) => server.starting) ? '#f59e0b' : '#b78383',
          }}>
            {servers.some((server) => server.starting)
              ? 'Local inference servers are loading. Engine features are enabled when both are ready.'
              : 'Some servers are down. Engine features are disabled until all servers are running.'}
          </p>
        </div>
      )}

      {/* Server list */}
      <div className="space-y-3">
        {servers.map((s) => (
          <div
            key={s.key}
            className="flex items-center justify-between px-4 py-3 rounded-lg border"
            style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            <div className="flex items-center gap-3">
              <span
                className="w-2.5 h-2.5 rounded-full flex-0"
                style={{
                  background: s.running ? '#10b981' : s.starting ? '#f59e0b' : '#b78383',
                  boxShadow: s.running ? '0 0 8px #10b98188' : s.starting ? '0 0 8px #f59e0b88' : 'none',
                }}
              />
              <div>
                <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 12, color: 'var(--pr-text)', marginBottom: 1 }}>
                  {s.label}
                </p>
                <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 10, color: 'var(--pr-text-dim)' }}>
                  localhost:{s.port}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <span
                className="text-[10px] font-mono px-2 py-0.5 rounded border"
                style={{
                  color: s.running ? '#10b981' : s.starting ? '#f59e0b' : '#b78383',
                  borderColor: s.running ? '#10b98155' : s.starting ? '#f59e0b55' : '#b7838355',
                  background: s.running ? '#10b98115' : s.starting ? '#f59e0b15' : '#b7838315',
                }}
              >
                {s.running ? 'running' : s.starting ? 'starting' : 'stopped'}
              </span>
              <button
                onClick={() => launchOne(s.key)}
                disabled={disabled || s.running || s.starting || launchingKey === s.key}
                className="px-2 py-1 rounded border text-[10px] font-mono transition-all"
                style={{
                  color: disabled || s.running || s.starting || launchingKey === s.key ? 'var(--pr-text-dim)' : '#10b981',
                  borderColor: disabled || s.running || s.starting || launchingKey === s.key ? 'var(--pr-border)' : '#10b98155',
                  background: 'transparent',
                  cursor: disabled || s.running || s.starting || launchingKey === s.key ? 'not-allowed' : 'pointer',
                  opacity: disabled || s.running || s.starting || launchingKey === s.key ? 0.5 : 1,
                }}
              >
                {launchingKey === s.key ? 'starting...' : 'start'}
              </button>
              <button
                onClick={() => stopOne(s.key)}
                disabled={disabled || !s.running || stopping}
                className="px-2 py-1 rounded border text-[10px] font-mono transition-all"
                style={{
                  color: disabled || !s.running || stopping ? 'var(--pr-text-dim)' : '#b78383',
                  borderColor: disabled || !s.running || stopping ? 'var(--pr-border)' : '#b7838355',
                  background: 'transparent',
                  cursor: disabled || !s.running || stopping ? 'not-allowed' : 'pointer',
                  opacity: disabled || !s.running || stopping ? 0.5 : 1,
                }}
              >
                {stoppingKey === s.key ? 'stopping...' : 'stop'}
              </button>
            </div>
          </div>
        ))}
      </div>

      {/* Bulk action buttons */}
      <div className="flex items-center gap-3 pt-2">
        <button
          onClick={launch}
          disabled={disabled || launching || engineEnabled}
          className="flex items-center gap-2 px-4 py-2 rounded border transition-all"
          style={{
            fontFamily: 'var(--pr-font-mono)',
            fontSize: 12,
            color: disabled || launching || engineEnabled ? 'var(--pr-text-dim)' : '#10b981',
            borderColor: disabled || launching || engineEnabled ? 'var(--pr-border)' : '#10b98155',
            background: 'transparent',
            cursor: disabled || launching || engineEnabled ? 'not-allowed' : 'pointer',
            opacity: disabled || launching || engineEnabled ? 0.5 : 1,
          }}
        >
          {launching ? 'launching...' : engineEnabled ? 'all servers running' : 'launch all servers'}
        </button>
        <button
          onClick={stop}
          disabled={disabled || stopping || !engineEnabled}
          className="flex items-center gap-2 px-4 py-2 rounded border transition-all"
          style={{
            fontFamily: 'var(--pr-font-mono)',
            fontSize: 12,
            color: disabled || stopping || !engineEnabled ? 'var(--pr-text-dim)' : '#b78383',
            borderColor: disabled || stopping || !engineEnabled ? 'var(--pr-border)' : '#b7838355',
            background: 'transparent',
            cursor: disabled || stopping || !engineEnabled ? 'not-allowed' : 'pointer',
            opacity: disabled || stopping || !engineEnabled ? 0.5 : 1,
          }}
        >
          {stopping ? 'stopping...' : 'stop all servers'}
        </button>
      </div>

      {/* Info */}
      <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 10, color: 'var(--pr-text-dim)', marginTop: 8, lineHeight: 1.5 }}>
        Servers are polled every 15 seconds. Auto-start attempts on page load. Stopping disables all engine features.
      </p>
    </SectionCard>
  );
}
