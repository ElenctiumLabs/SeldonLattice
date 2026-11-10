import React, { useMemo } from 'react'
import type { SigilAlgorithm } from '../lib/api'

interface SigilCanvasProps {
  cid: string
  algorithm?: SigilAlgorithm
  size?: number
  className?: string
  color?: string
  grain?: number
}

function hashStringToBytes(hash: string): number[] {
  const bytes: number[] = []
  for (let i = 0; i < hash.length; i += 2) {
    bytes.push(parseInt(hash.substring(i, i + 2), 16) || 0)
  }
  return bytes
}

function lissajous(bytes: number[], size: number, grain: number, color: string): string {
  const cx = size / 2
  const cy = size / 2
  const a = 3 + (bytes[0] % 4)
  const b = 2 + (bytes[1] % 5)
  const delta = (bytes[2] / 255) * Math.PI
  const r = size * 0.38
  const steps = 100 + grain * 30
  const pts: string[] = []

  for (let i = 0; i <= steps; i++) {
    const t = (i / steps) * Math.PI * 2
    const x = cx + r * Math.sin(a * t + delta)
    const y = cy + r * Math.sin(b * t)
    pts.push(`${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`)
  }

  return `<path d="${pts.join(' ')}" fill="none" stroke="${color}" stroke-width="1.2" stroke-linecap="round" opacity="0.8"/>`
}

function mandala(bytes: number[], size: number, grain: number, color: string): string {
  const cx = size / 2
  const cy = size / 2
  const layers = 3 + (bytes[0] % 3) + Math.min(grain, 3)
  const paths: string[] = []

  for (let l = 0; l < layers; l++) {
    const petals = 4 + l * 2 + (bytes[(l + 3) % bytes.length] % 4)
    const r = size * (0.12 + l * 0.1)
    for (let p = 0; p < petals; p++) {
      const a = (p / petals) * Math.PI * 2
      const px = cx + Math.cos(a) * r
      const py = cy + Math.sin(a) * r
      const cr = r * 0.4 * (0.5 + (bytes[(l + p) % bytes.length] / 255) * 0.5)
      paths.push(
        `<circle cx="${px.toFixed(1)}" cy="${py.toFixed(1)}" r="${cr.toFixed(1)}" fill="none" stroke="${color}" stroke-width="0.8" opacity="${0.5 + l * 0.1}"/>`
      )
    }
  }

  return paths.join('')
}

function fractalTree(bytes: number[], size: number, grain: number, color: string): string {
  const paths: string[] = []
  const maxDepth = 3 + Math.min(grain, 4)

  function branch(x: number, y: number, angle: number, depth: number, len: number) {
    if (depth > maxDepth || len < 2) return
    const b = bytes[depth % bytes.length]
    const x2 = x + Math.cos(angle) * len
    const y2 = y + Math.sin(angle) * len
    paths.push(
      `<line x1="${x.toFixed(1)}" y1="${y.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="${color}" stroke-width="${Math.max(0.5, 2 - depth * 0.3)}" stroke-linecap="round" opacity="${0.8 - depth * 0.08}"/>`
    )
    const spread = 0.3 + (b / 255) * 0.5
    branch(x2, y2, angle - spread, depth + 1, len * 0.68)
    branch(x2, y2, angle + spread, depth + 1, len * 0.68)
  }

  branch(size / 2, size * 0.85, -Math.PI / 2, 0, size * 0.25)
  return paths.join('')
}

const ALGORITHMS: Record<string, (bytes: number[], size: number, grain: number, color: string) => string> = {
  lissajous: lissajous,
  mandala: mandala,
  fractal_tree: fractalTree,
}

export const SigilCanvas: React.FC<SigilCanvasProps> = React.memo(function SigilCanvas({
  cid,
  algorithm = 'fractal_tree',
  size = 200,
  className = '',
  color = '#fbbf24',
  grain = 3,
}) {
  const svgContent = useMemo(() => {
    const bytes = hashStringToBytes(cid)
    const renderer = ALGORITHMS[algorithm] || fractalTree
    return renderer(bytes, size, grain, color)
  }, [cid, algorithm, size, grain, color])

  return (
    <div className={`inline-flex items-center justify-center ${className}`}>
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        xmlns="http://www.w3.org/2000/svg"
        className="rounded"
        style={{ background: 'var(--pr-bg)' }}
      >
        <rect width={size} height={size} fill="var(--pr-bg)" rx="4" />
        <g dangerouslySetInnerHTML={{ __html: svgContent }} />
      </svg>
    </div>
  )
})