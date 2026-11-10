import React, { useEffect, useRef, useCallback } from 'react'
import type { LiveTurn } from '../types/liveTurn'
import { getConfig } from '../lib/config'

interface ReplayControlsProps {
  turns: LiveTurn[]
  isPlaying: boolean
  currentIndex: number
  speed: number
  onPlay: () => void
  onPause: () => void
  onStepForward: () => void
  onStepBack: () => void
  onReset: () => void
  onSpeedChange: (speed: number) => void
  onJumpTo: (index: number) => void
}

export const ReplayControls: React.FC<ReplayControlsProps> = ({
  turns,
  isPlaying,
  currentIndex,
  speed,
  onPlay,
  onPause,
  onStepForward,
  onStepBack,
  onReset,
  onSpeedChange,
  onJumpTo,
}) => {
  const progress = turns.length > 0 ? ((currentIndex + 1) / turns.length) * 100 : 0

  return (
    <div
      className="absolute bottom-16 left-1/2 -translate-x-1/2 z-30 rounded-xl px-4 py-3 flex items-center gap-3 select-none backdrop-blur-md shadow-2xl pr-glass-surface"
      style={{
        backgroundColor: 'var(--pr-surface)',
        border: '1px solid var(--pr-border)',
        boxShadow: '0 12px 36px rgba(0,0,0,0.25)',
      }}
    >
      {/* Reset */}
      <button
        onClick={onReset}
        className="p-1.5 rounded transition-colors cursor-pointer"
        style={{ color: 'var(--pr-text-muted)' }}
        title="Reset"
      >
        <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M1 4v6h6M23 20v-6h-6" />
          <path d="M20.49 9A9 9 0 005.64 5.64L1 10m22 4l-4.64 4.36A9 9 0 013.51 15" />
        </svg>
      </button>

      {/* Step Back */}
      <button
        onClick={onStepBack}
        disabled={currentIndex <= 0}
        className="p-1.5 rounded transition-colors cursor-pointer disabled:opacity-30 disabled:cursor-not-allowed"
        style={{ color: 'var(--pr-text-muted)' }}
        title="Previous turn"
      >
        <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <polygon points="19,20 9,12 19,4" /><line x1="5" y1="19" x2="5" y2="5" />
        </svg>
      </button>

      {/* Play/Pause */}
      <button
        onClick={isPlaying ? onPause : onPlay}
        className="p-2 rounded-full transition-all cursor-pointer shadow-sm"
        style={{
          backgroundColor: isPlaying ? 'var(--pr-role-question)' : 'var(--pr-role-proposer)',
          color: isPlaying ? '#ffffff' : 'var(--pr-bg)',
        }}
        title={isPlaying ? 'Pause' : 'Play'}
      >
        {isPlaying ? (
          <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor">
            <rect x="6" y="4" width="4" height="16" /><rect x="14" y="4" width="4" height="16" />
          </svg>
        ) : (
          <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor">
            <polygon points="5,3 19,12 5,21" />
          </svg>
        )}
      </button>

      {/* Step Forward */}
      <button
        onClick={onStepForward}
        disabled={currentIndex >= turns.length - 1}
        className="p-1.5 rounded transition-colors cursor-pointer disabled:opacity-30 disabled:cursor-not-allowed"
        style={{ color: 'var(--pr-text-muted)' }}
        title="Next turn"
      >
        <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <polygon points="5,4 15,12 5,20" /><line x1="19" y1="5" x2="19" y2="19" />
        </svg>
      </button>

      {/* Speed */}
      <div className="flex items-center gap-1 ml-2 border-l pl-3" style={{ borderColor: 'var(--pr-border)' }}>
        {[0.5, 1, 2, 4].map((s) => (
          <button
            key={s}
            onClick={() => onSpeedChange(s)}
            className="px-1.5 py-0.5 rounded text-[10px] font-mono transition-all cursor-pointer"
            style={{
              backgroundColor: speed === s ? 'var(--pr-role-proposer)' : 'transparent',
              color: speed === s ? 'var(--pr-bg)' : 'var(--pr-text-dim)',
              fontWeight: speed === s ? 700 : 400,
            }}
          >
            {s}x
          </button>
        ))}
      </div>

      {/* Counter */}
      <div className="text-[11px] font-mono ml-2 min-w-12.5 text-center" style={{ color: 'var(--pr-text-muted)' }}>
        {currentIndex + 1}/{turns.length}
      </div>

      {/* Progress bar */}
      <div
        className="w-24 h-1.5 rounded-full overflow-hidden ml-1 cursor-pointer"
        style={{ backgroundColor: 'var(--pr-surface-raised)' }}
        onClick={(e) => {
          const rect = e.currentTarget.getBoundingClientRect()
          const pct = (e.clientX - rect.left) / rect.width
          const idx = Math.round(pct * (turns.length - 1))
          onJumpTo(idx)
        }}
      >
        <div
          className="h-full rounded-full transition-all duration-200 pointer-events-none"
          style={{ width: `${progress}%`, backgroundColor: 'var(--pr-role-proposer)' }}
        />
      </div>
    </div>
  );
}

/** Hook that manages replay state and auto-play timing */
export function useReplay(turns: LiveTurn[]) {
  const [isPlaying, setIsPlaying] = React.useState(false)
  const [currentIndex, setCurrentIndex] = React.useState(0)
  const [speed, setSpeed] = React.useState(1)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const clearTimer = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }, [])

  const play = useCallback(() => {
    setIsPlaying(true)
  }, [])

  const pause = useCallback(() => {
    setIsPlaying(false)
    clearTimer()
  }, [clearTimer])

  const stepForward = useCallback(() => {
    setCurrentIndex((i) => Math.min(i + 1, turns.length - 1))
  }, [turns.length])

  const stepBack = useCallback(() => {
    setCurrentIndex((i) => Math.max(i - 1, 0))
  }, [])

  const reset = useCallback(() => {
    setIsPlaying(false)
    clearTimer()
    setCurrentIndex(0)
  }, [clearTimer])

  const jumpTo = useCallback((index: number) => {
    setCurrentIndex(Math.max(0, Math.min(index, turns.length - 1)))
  }, [turns.length])

  // Auto-advance when playing
  useEffect(() => {
    if (!isPlaying) return
    if (currentIndex >= turns.length - 1) {
      setIsPlaying(false)
      return
    }

    const delay = getConfig().replayBaseDelayMs / speed
    timerRef.current = setTimeout(() => {
      setCurrentIndex((i) => {
        if (i >= turns.length - 1) {
          setIsPlaying(false)
          return i
        }
        return i + 1
      })
    }, delay)

    return clearTimer
  }, [isPlaying, currentIndex, turns.length, speed, clearTimer])

  // Visible turns: all turns up to currentIndex
  const visibleTurns = turns.slice(0, currentIndex + 1)

  return {
    isPlaying,
    currentIndex,
    speed,
    visibleTurns,
    play,
    pause,
    stepForward,
    stepBack,
    reset,
    jumpTo,
    setSpeed,
  }
}
