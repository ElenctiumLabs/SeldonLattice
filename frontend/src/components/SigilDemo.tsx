import React, { useState } from 'react'
import { SigilCanvas } from './SigilCanvas'
import type { SigilAlgorithm } from '../lib/api'

const ALGORITHMS: { value: SigilAlgorithm; label: string }[] = [
  { value: 'lissajous', label: 'Lissajous' },
  { value: 'mandala', label: 'Mandala' },
  { value: 'fractal_tree', label: 'Fractal Tree' },
]

const SAMPLE_CIDS = [
  'bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi',
  'QmXoypizjW3WknFiJnKLwHCnL72vedxjQkDDP1mXWo6uco',
  'bafybeihkvaggf4d4dlsclvkbq3t4tjqh4t5a5mzpxbcgq3q3q3q3q3q3q',
]

export const SigilDemo: React.FC = () => {
  const [cid, setCid] = useState(SAMPLE_CIDS[0])
  const [grain, setGrain] = useState(3)

  return (
    <div className="min-h-screen bg-[#080a0f] text-[#f1f5f9] p-6 md:p-10">
      <div className="max-w-6xl mx-auto">
        <h1 className="text-2xl font-mono uppercase tracking-widest text-[#f1f5f9] mb-2">
          Sigil Algorithms
        </h1>
        <p className="text-sm text-[#8b9bb4] font-mono mb-8">
          Cryptographic hash visualization - each CID produces a unique sigil
        </p>

        {/* Controls */}
        <div className="flex flex-wrap items-end gap-4 mb-8 p-4 bg-[#11151c] border border-[rgba(255,255,255,0.08)] rounded">
          <div className="flex-1 min-w-50">
            <label className="block text-[11px] font-mono uppercase tracking-widest text-[#8b9bb4] mb-1">
              Hash Input
            </label>
            <input
              type="text"
              value={cid}
              onChange={(e) => setCid(e.target.value)}
              className="w-full bg-[#080a0f] border border-[rgba(255,255,255,0.08)] rounded px-3 py-2 text-[13px] font-mono text-[#f1f5f9] focus:border-[#fbbf24] outline-none transition-colors"
              placeholder="Enter a CID or hash..."
            />
          </div>
          <div>
            <label className="block text-[11px] font-mono uppercase tracking-widest text-[#8b9bb4] mb-1">
              Grain ({grain})
            </label>
            <input
              type="range"
              min={1}
              max={8}
              value={grain}
              onChange={(e) => setGrain(Number(e.target.value))}
              className="w-32 accent-[#fbbf24] cursor-pointer"
            />
          </div>
          <div className="flex gap-1.5">
            {SAMPLE_CIDS.map((s, i) => (
              <button
                key={i}
                onClick={() => setCid(s)}
                className="px-2 py-1.5 rounded text-[11px] font-mono border transition-all cursor-pointer"
                style={{
                  backgroundColor: cid === s ? '#fbbf24' : '#11151c',
                  color: cid === s ? '#080a0f' : '#8b9bb4',
                  borderColor: cid === s ? '#fbbf24' : 'rgba(255,255,255,0.08)',
                }}
              >
                Sample {i + 1}
              </button>
            ))}
          </div>
        </div>

        {/* Algorithm Grid */}
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-6">
          {ALGORITHMS.map((algo) => (
            <div
              key={algo.value}
              className="flex flex-col items-center gap-3 p-4 bg-[#11151c] border border-[rgba(255,255,255,0.08)] rounded"
            >
              <SigilCanvas
                cid={cid}
                algorithm={algo.value}
                size={160}
                grain={grain}
              />
              <span className="text-[12px] font-mono uppercase tracking-widest text-[#8b9bb4]">
                {algo.label}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
