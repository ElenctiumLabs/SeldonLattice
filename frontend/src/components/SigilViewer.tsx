import React, { useState, useCallback } from 'react'
import { SigilCanvas } from './SigilCanvas'
import type { SigilAlgorithm } from '../lib/api'

interface SigilViewerProps {
  cid: string
  className?: string
}

const ALGO_OPTIONS: { value: SigilAlgorithm; label: string }[] = [
  { value: 'lissajous', label: 'Lissajous' },
  { value: 'mandala', label: 'Mandala' },
  { value: 'fractal_tree', label: 'Fractal Tree' },
]

export const SigilViewer: React.FC<SigilViewerProps> = ({ cid, className = '' }) => {
  const [algorithm, setAlgorithm] = useState<SigilAlgorithm>('fractal_tree')
  const [grain, setGrain] = useState(3)
  useCallback(async () => {
    try {
      await navigator.clipboard.writeText(cid)
    } catch {
      // Fallback
      const ta = document.createElement('textarea')
      ta.value = cid
      document.body.appendChild(ta)
      ta.select()
      document.execCommand('copy')
      document.body.removeChild(ta)
    }
  }, [cid]);
  return (
    <div className={`flex w-full flex-col items-stretch gap-4 ${className}`}>
      <SigilCanvas
        cid={cid}
        algorithm={algorithm}
        size={320}
        grain={grain}
        className="self-center"
      />

      <div className="w-full space-y-3">
        <div>
          <label className="block text-[12px] font-mono uppercase tracking-widest mb-1" style={{ color: 'var(--pr-text-muted)' }}>            Algorithm
          </label>
          <select
            value={algorithm}
            onChange={(e) => setAlgorithm(e.target.value as SigilAlgorithm)}
            className="w-full bg-(--pr-surface) border border-(--pr-border) rounded px-3 py-2 text-[13px] font-mono text-(--pr-text) cursor-pointer focus:border-(--pr-role-proposer) outline-none transition-colors"
          >
            {ALGO_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-[12px] font-mono uppercase tracking-widest mb-1" style={{ color: 'var(--pr-text-muted)' }}>            Grain ({grain})
          </label>
          <input
            type="range"
            min={1}
            max={8}
            value={grain}
            onChange={(e) => setGrain(Number(e.target.value))}
            className="w-full accent-[#fbbf24] cursor-pointer"
          />
        </div>
      </div>
    </div>
  )
}
