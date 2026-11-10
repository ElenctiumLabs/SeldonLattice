import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { lazy, Suspense } from 'react'
import { loadFrontendConfig } from './lib/config'

const App = lazy(() => import('./App.tsx'));

// -- Global error handlers ------------------------------------------------------
window.addEventListener('unhandledrejection', (event) => {
  console.error('[Unhandled Promise Rejection]', event.reason);
  event.preventDefault();
});

window.addEventListener('error', (event) => {
  console.error('[Global Error]', event.error || event.message, event.filename, event.lineno);
});

const root = createRoot(document.getElementById('root')!);

function FatalBootError({ message }: { message: string }) {
  return (
    <div className="flex items-center justify-center h-dvh p-6" style={{ background: '#080a0f', color: '#f1f5f9' }}>
      <div className="max-w-2xl w-full rounded-xl p-6" style={{ border: '1px solid rgba(224,122,95,0.45)', background: 'rgba(185,90,58,0.10)' }}>
        <h1 className="text-[16px] font-mono uppercase tracking-widest mb-3" style={{ color: '#e07a5f' }}>
          Frontend configuration unavailable
        </h1>
        <p className="text-[12px] font-mono mb-4" style={{ color: '#e07a5f' }}>
          The UI will not start on hard-coded defaults. Populate FrontendConfig in appsettings and retry.
        </p>
        <pre className="text-[12px] font-mono whitespace-pre-wrap mb-5" style={{ color: '#f1f5f9' }}>
          {message}
        </pre>
        <button
          onClick={() => window.location.reload()}
          className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider cursor-pointer"
          style={{ background: '#e07a5f', color: '#080a0f' }}
        >
          Reload
        </button>
      </div>
    </div>
  );
}

async function boot() {
  try {
    await loadFrontendConfig();

    // Imported after the config is available: the API client resolves its base URL and
    // timeouts from getConfig() at module construction time.
    const { ServerStatusProvider } = await import('./hooks/useServerStatus');

    root.render(
      <StrictMode>
        <Suspense
          fallback={
            <div className="flex items-center justify-center h-dvh bg-[#080a0f] text-[#f1f5f9]">
              <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-[#f1f5f9]"></div>
            </div>
          }
        >
          <ServerStatusProvider>
            <App />
          </ServerStatusProvider>
        </Suspense>
      </StrictMode>,
    );
  } catch (error) {
    root.render(<FatalBootError message={error instanceof Error ? error.message : String(error)} />);
  }
}

void boot();
