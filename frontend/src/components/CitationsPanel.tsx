import { useEffect, useState } from 'react'
import { ROUTES } from '../lib/api'
import type { Citation } from '../lib/types'

const API_BASE = import.meta.env.VITE_API_BASE || ''

export default function CitationsPanel({ onClose }: { onClose: () => void }) {
  const [query, setQuery] = useState('contract breach')
  const [topN, setTopN] = useState(5)
  const [loading, setLoading] = useState(false)
  const [results, setResults] = useState<Citation[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const handler = () => {
      // focus or prefill if needed
    }
    window.addEventListener('openCitationsPanel', handler)
    return () => window.removeEventListener('openCitationsPanel', handler)
  }, [])

  async function fetchCitations() {
    setLoading(true)
    setError(null)
    setResults(null)
    try {
      const url = `${API_BASE}${ROUTES.ingestionCitations}?q=${encodeURIComponent(query)}&topN=${topN}`
      const headers: Record<string,string> = {}
      const key = import.meta.env.VITE_CITATION_API_KEY
      if (key) headers['X-Api-Key'] = key

      const res = await fetch(url, { headers })
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
      const data = (await res.json()) as Citation[]
      setResults(data)
    } catch (ex: any) {
      setError(ex?.message ?? String(ex))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50 backdrop-blur-sm" onClick={onClose} />
      <div
        className="relative rounded-xl w-[min(960px,96%)] max-h-[80vh] overflow-auto p-6 animate-fade-in-up pr-glass-surface"
        style={{
          background: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          color: 'var(--pr-text)',
          boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
        }}
      >
        <div className="flex items-center justify-between mb-4 border-b pb-3" style={{ borderColor: 'var(--pr-border)' }}>
          <h3 className="text-lg font-semibold" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>Citation Lookup</h3>
          <button
            onClick={onClose}
            className="px-3 py-1 rounded border text-xs font-mono transition-colors cursor-pointer"
            style={{ borderColor: 'var(--pr-border)', color: 'var(--pr-text-muted)', background: 'var(--pr-surface-raised)' }}
          >
            Close
          </button>
        </div>

        <div className="flex gap-2 mb-4">
          <input
            className="flex-1 px-3 py-2 rounded border font-mono text-sm"
            style={{ background: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search citations..."
          />
          <input
            type="number"
            min={1}
            max={20}
            value={topN}
            onChange={(e) => setTopN(Number((e.target as HTMLInputElement).value))}
            className="w-24 px-2 py-2 rounded border font-mono text-sm"
            style={{ background: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
          />
          <button
            onClick={fetchCitations}
            className="px-4 py-2 rounded font-mono text-sm font-semibold transition-all cursor-pointer"
            style={{ background: 'var(--pr-role-proposer)', color: 'var(--pr-bg)' }}
          >
            Lookup
          </button>
        </div>

        {loading && <div className="text-sm font-mono" style={{ color: 'var(--pr-text-muted)' }}>Loading…</div>}
        {error && <div className="text-sm font-mono text-red-500">{error}</div>}

        {results && (
          <ul className="space-y-3">
            {results.map((c) => (
              <li
                key={c.id}
                className="p-3 rounded-lg border transition-all"
                style={{ background: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)' }}
              >
                <a href={c.url} target="_blank" rel="noreferrer" className="font-medium hover:underline" style={{ color: 'var(--pr-role-proposer)' }}>
                  {c.title}
                </a>
                <div className="text-xs font-mono mt-1" style={{ color: 'var(--pr-text-muted)' }}>
                  {c.sourceId} - Confidence: {c.confidence?.toFixed(2)}
                </div>
              </li>
            ))}
          </ul>
        )}

        {!results && !loading && <div className="text-sm font-mono" style={{ color: 'var(--pr-text-dim)' }}>No results yet - enter a query and click Lookup.</div>}
      </div>
    </div>
  );
}
