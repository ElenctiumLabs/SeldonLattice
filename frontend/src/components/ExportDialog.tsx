import React, { useState, useMemo } from 'react'
import { PrintView } from './PrintView'
import { getConfig } from '../lib/config'
import { api } from '../lib/api'
import type { LiveTurn } from '../types/liveTurn'

interface ExportDialogProps {
  isOpen: boolean
  onClose: () => void
  sessionId: string | null
  theme: string
  liveTurns: LiveTurn[]
}

type ExportFormat = 'markdown' | 'json'

function formatTimestamp(ts: string | number): string {
  try {
    const d = new Date(typeof ts === 'number' ? ts : ts)
    return d.toLocaleString()
  } catch {
    return String(ts)
  }
}

function toMarkdown(turns: LiveTurn[], theme: string, sessionId: string): string {
  const lines: string[] = []
  lines.push(`# Seldon Lattice - Debate Transcript`)
  lines.push('')
  lines.push(`**Theme:** ${theme}`)
  lines.push(`**Session:** ${sessionId}`)
  lines.push(`**Exported:** ${new Date().toLocaleString()}`)
  lines.push(`**Turns:** ${turns.length}`)
  lines.push('')
  lines.push('---')
  lines.push('')

  let currentLevel = -1
  for (const turn of turns) {
    const level = turn.level ?? 0
    if (level !== currentLevel) {
      currentLevel = level
      lines.push(`## Round ${level + 1}`)
      lines.push('')
    }

    const speaker = turn.speaker.toUpperCase()
    const role = turn.dialecticRole
    const ts = formatTimestamp(turn.timestamp)

    lines.push(`### ${speaker} (${role})`)
    lines.push(`*${ts}*`)
    lines.push('')

    if (turn.metrics) {
      const m = turn.metrics
      const parts: string[] = []
      if (m.qualityScore != null) parts.push(`Quality: ${m.qualityScore.toFixed(1)}%`)
      if (m.ceremonyScore != null) parts.push(`Ceremony: ${m.ceremonyScore.toFixed(1)}/8`)
      if (m.similarityScore != null) parts.push(`Similarity: ${m.similarityScore.toFixed(2)}`)
      if (m.ascentScore != null) parts.push(`Ascent: ${m.ascentScore.toFixed(3)}`)
      if (m.joinMediation != null) parts.push(`Join mediation: ${m.joinMediation.toFixed(3)}`)
      if (m.joinNovelty != null) parts.push(`Join novelty: ${m.joinNovelty.toFixed(3)}`)
      if (m.irreducibleTension != null) parts.push(`Irreducible tension: ${m.irreducibleTension.toFixed(3)}`)
      if (m.variabilityScore != null) parts.push(`Variability: ${m.variabilityScore.toFixed(3)}`)
      if (m.newArgumentCount != null) parts.push(`New arguments: ${m.newArgumentCount}`)
      if (m.wordCount != null) parts.push(`Word count: ${m.wordCount}`)
      if (m.selfSimilarity != null) parts.push(`Self similarity: ${m.selfSimilarity.toFixed(3)}`)
      if (parts.length > 0) {
        lines.push(`> ${parts.join(' · ')}`)
        lines.push('')
      }
    }

    if (turn.isSustained) { lines.push('> **Sustained theme**'); lines.push('') }
    if (turn.remainingDissensus) { lines.push(`> **Remaining dissensus:** ${turn.remainingDissensus}`); lines.push('') }
    if (turn.repeatsPremise != null) { lines.push(`> **Repeats premise:** ${(turn.repeatsPremise * 100).toFixed(1)}%`); lines.push('') }
    if (turn.unresolvedTensions?.length) { lines.push(`> **Unresolved tensions:** ${turn.unresolvedTensions.join('; ')}`); lines.push('') }
    if (turn.tensionIntensity != null) { lines.push(`> **Tension intensity:** ${turn.tensionIntensity.toFixed(3)}`); lines.push('') }

    lines.push(turn.content)
    lines.push('')
  }

  return lines.join('\n')
}

function toJSON(turns: LiveTurn[], theme: string, sessionId: string): string {
  return JSON.stringify({
    exportVersion: '1.1',
    exportedAt: new Date().toISOString(),
    sessionId,
    session: {
      sessionId,
    },
    theme,
    turnCount: turns.length,
    turns: turns.map((t) => ({
      id: t.id,
      speaker: t.speaker,
      dialecticRole: t.dialecticRole,
      level: t.level,
      phase: t.phase,
      timestamp: t.timestamp,
      content: t.content,
      metrics: t.metrics ?? null,
      markers: {
        isSustained: t.isSustained ?? false,
        remainingDissensus: t.remainingDissensus ?? null,
        repeatsPremise: t.repeatsPremise ?? null,
        unresolvedTensions: t.unresolvedTensions ?? null,
        tensionIntensity: t.tensionIntensity ?? null,
      },
      reasoningTrace: t.reasoningTrace ?? null,
    })),
  }, null, 2)
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

function downloadFile(content: string, filename: string, mimeType: string) {
  downloadBlob(new Blob([content], { type: mimeType }), filename)
}

export const ExportDialog: React.FC<ExportDialogProps> = ({
  isOpen,
  onClose,
  sessionId,
  theme,
  liveTurns,
}) => {
  const [format, setFormat] = useState<ExportFormat>('markdown')
  const [includeMetrics, setIncludeMetrics] = useState(true)
  const [includeVerify, setIncludeVerify] = useState(false)
  const [copied, setCopied] = useState(false)
  const [busy, setBusy] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const effectiveSessionId = useMemo(() => {
    if (sessionId && sessionId !== 'unknown') return sessionId;
    try {
      const stored = localStorage.getItem('seldon_active_session_id');
      if (stored && stored !== 'unknown') return stored;
    } catch {}
    return sessionId || 'unknown';
  }, [sessionId]);

  const preview = useMemo(() => {
    const turns = includeMetrics ? liveTurns : liveTurns.map((t) => ({ ...t, metrics: undefined }))
    if (format === 'markdown') return toMarkdown(turns, theme, effectiveSessionId)
    return toJSON(turns, theme, effectiveSessionId)
  }, [format, includeMetrics, liveTurns, theme, effectiveSessionId])

  if (!isOpen) return null

  const handleDownload = async () => {
    const ext = format === 'markdown' ? 'md' : 'json'
    const mime = format === 'markdown' ? 'text/markdown' : 'application/json'
    const filename = `seldon-lattice-${effectiveSessionId !== 'unknown' ? effectiveSessionId : 'export'}.${ext}`

    // Row 5: when JSON export requests ML-DSA integrity verification, fetch the
    // server-side machine-verifiable export (nodes + verify block) so the file
    // can be audited offline with `dotnet run --project src/SeldonLattice.API verify`.
    if (format === 'json' && includeVerify && effectiveSessionId && effectiveSessionId !== 'unknown') {
      setBusy(true)
      setDownloadError(null)
      try {
        const blob = await api.exportSession(effectiveSessionId, 'json')
        downloadBlob(blob, filename)
        return
      } catch (err) {
        setDownloadError(err instanceof Error ? err.message : String(err))
      } finally {
        setBusy(false)
      }
      return
    }

    downloadFile(preview, filename, mime)
  }

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(preview)
      setCopied(true)
      setTimeout(() => setCopied(false), getConfig().copiedIndicatorMs)
    } catch {
      setCopied(false)
    }
  }

  const handlePrint = () => {
    const container = document.createElement('div')
    container.id = 'seldon-print-container'
    container.style.position = 'fixed'
    container.style.top = '0'
    container.style.left = '0'
    container.style.width = '100%'
    container.style.height = '100%'
    container.style.zIndex = '9999'
    container.style.background = 'white'
    document.body.appendChild(container)

    // Use createPortal approach via direct DOM manipulation for print
    import('react-dom/client').then(({ createRoot }) => {
      const root = createRoot(container)
      root.render(
        <PrintView sessionId={effectiveSessionId} theme={theme} liveTurns={liveTurns} />
      )

      let cleanedUp = false
      const cleanup = () => {
        if (cleanedUp) return
        cleanedUp = true
        window.removeEventListener('afterprint', cleanup)
        root.unmount()
        container.remove()
      }

      // `afterprint` is the reliable signal that the print preview has closed. The
      // timeout is only a fallback for embedded webviews that never fire it.
      window.addEventListener('afterprint', cleanup, { once: true })

      const printNow = () => {
        try {
          window.print()
        } finally {
          setTimeout(cleanup, 10_000)
        }
      }

      // Wait for layout and fonts so the print engine paginates the final DOM, not a
      // partially rendered report.
      const fontsReady = document.fonts?.ready ?? Promise.resolve()
      fontsReady.then(() => setTimeout(printNow, 100))
    })
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
      onClick={onClose}
    >
      <div
        className="rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col animate-fade-in-up pr-glass-surface"
        style={{
          backgroundColor: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          color: 'var(--pr-text)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-5 py-4 border-b flex items-center justify-between flex-0" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
          <h2 className="text-[13px] font-mono tracking-widest uppercase font-bold" style={{ color: 'var(--pr-text)' }}>
            EXPORT DEBATE
          </h2>
          <button
            onClick={onClose}
            className="w-8 h-8 rounded flex items-center justify-center transition-colors cursor-pointer text-lg font-mono"
            style={{ color: 'var(--pr-text-dim)', border: '1px solid var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            ×
          </button>
        </div>

        {/* Controls */}
        <div className="px-5 py-4 border-b space-y-3 flex-0" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)' }}>
          <div className="flex items-center gap-4">
            <label className="text-[11px] font-mono uppercase tracking-widest" style={{ color: 'var(--pr-text-muted)' }}>
              Format
            </label>
            <div className="flex gap-1.5">
              {(['markdown', 'json'] as ExportFormat[]).map((f) => (
                <button
                  key={f}
                  onClick={() => setFormat(f)}
                  className="px-3 py-1.5 rounded text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer border"
                  style={{
                    backgroundColor: format === f ? 'var(--pr-role-proposer)' : 'var(--pr-surface-raised)',
                    color: format === f ? 'var(--pr-bg)' : 'var(--pr-text-muted)',
                    borderColor: format === f ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                  }}
                >
                  {f}
                </button>
              ))}
            </div>
          </div>

          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={includeMetrics}
              onChange={(e) => setIncludeMetrics(e.target.checked)}
              className="accent-(--pr-role-proposer) w-4 h-4"
            />
            <span className="text-[13px] font-mono" style={{ color: 'var(--pr-text)' }}>Include quality metrics</span>
          </label>

          {format === 'json' && (
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={includeVerify}
                onChange={(e) => setIncludeVerify(e.target.checked)}
                className="accent-(--pr-role-proposer) w-4 h-4"
              />
              <span className="text-[13px] font-mono" style={{ color: 'var(--pr-text)' }}>Include ML-DSA integrity verify block</span>
            </label>
          )}
        </div>

        {/* Preview */}
        <div className="flex-1 overflow-hidden px-5 py-3" style={{ backgroundColor: 'var(--pr-bg)' }}>
          <div className="text-[11px] font-mono uppercase tracking-widest mb-2" style={{ color: 'var(--pr-text-dim)' }}>
            PREVIEW
          </div>
          <pre className="h-full max-h-70 overflow-auto text-[12px] font-mono rounded p-3 whitespace-pre-wrap" style={{ backgroundColor: 'var(--pr-surface)', border: '1px solid var(--pr-border)', color: 'var(--pr-text)' }}>
            {preview.length > 3000 ? preview.substring(0, 3000) + '\n\n... (truncated)' : preview}
          </pre>
        </div>

        {/* Actions */}
        <div className="px-5 py-4 border-t flex items-center justify-end gap-3 flex-0" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
          <button
            onClick={handlePrint}
            className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer border"
            style={{ borderColor: 'var(--pr-border)', color: 'var(--pr-text)', background: 'var(--pr-surface)' }}
          >
            Print Report
          </button>
          <button
            onClick={handleCopy}
            className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer border"
            style={{
              backgroundColor: copied ? 'var(--pr-role-judge)' : 'var(--pr-surface)',
              color: copied ? '#ffffff' : 'var(--pr-text)',
              borderColor: copied ? 'var(--pr-role-judge)' : 'var(--pr-border)',
            }}
          >
            {copied ? 'Copied' : 'Copy'}
          </button>
          <button
            onClick={handleDownload}
            disabled={busy}
            className="px-4 py-2 rounded text-[12px] font-mono uppercase font-semibold tracking-wider transition-all cursor-pointer"
            style={{
              backgroundColor: 'var(--pr-role-proposer)',
              color: 'var(--pr-bg)',
              opacity: busy ? 0.6 : 1,
            }}
          >
            {busy ? 'Verifying…' : format === 'json' && includeVerify ? 'Download (ML-DSA)' : 'Download'}
          </button>
          {downloadError && (
            <div className="text-[11px] font-mono mt-2" style={{ color: 'var(--pr-role-judge)' }}>
              {downloadError}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
