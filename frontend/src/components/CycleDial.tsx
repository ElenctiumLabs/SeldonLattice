import React from 'react';
import { roleColor } from '../theme/prime-radiant-tokens';
import { getConfig } from '../lib/config';

interface CycleDialProps {
  currentTurn?: number;
  totalTurns?: number;
  currentPhase?: 'thesis' | 'antithesis' | 'synthesis' | 'gate' | string;
  isComplete?: boolean;
}

const PHASE_COLORS: Record<string, string> = {
  thesis: roleColor('question'),
  antithesis: roleColor('judge'),
  synthesis: roleColor('proposer'),
  gate: '#e74c3c',
};

export const CycleDial: React.FC<CycleDialProps> = ({
  currentTurn = 0,
  totalTurns = getConfig().defaultCycleTotalTurns,
  currentPhase = 'thesis',
  isComplete = false,
}) => {
  const cfg = getConfig();
  const size = cfg.cycleDialSize;
  const radius = cfg.cycleDialRadius;
  const circumference = 2 * Math.PI * radius;

  // Each turn gets an equal segment of the circle
  const segmentArcLength = circumference / totalTurns;
  // Small gap between segments (2% of segment)
  const gapLength = segmentArcLength * 0.02;
  const dashLength = segmentArcLength - gapLength;

  // Track dasharray: alternating dash/gap for each segment
  const trackDashArray = Array(totalTurns)
    .fill(`${dashLength} ${gapLength}`)
    .join(' ');

  // Progress: single dash that grows to fill completed segments
  // progressLength = completed segments * segmentArcLength
  const progressRatio = Math.min(1, currentTurn / totalTurns);
  const progressLength = circumference * progressRatio;
  // Progress dasharray: one long dash (progressLength) + huge gap (rest of circle)
  const progressDashArray = `${progressLength} ${circumference}`;

  const phaseColor = isComplete
    ? roleColor('proposer')
    : (PHASE_COLORS[currentPhase.toLowerCase()] ?? roleColor('question'));

  const phaseLabel = isComplete ? 'EQUILIBRIUM' : currentPhase.toUpperCase();

  return (
    <div
      className="flex flex-col items-center justify-center border-t select-none"
      style={{ minHeight: '250px', borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
    >
      <span className="text-[11px] font-mono tracking-widest uppercase mb-2 select-none font-bold" style={{ color: 'var(--pr-text)' }}>
        CYCLE PHASE
      </span>

      <div className="relative flex items-center justify-center" style={{ width: size, height: size }}>
        <div
          className={`absolute inset-0 rounded-full transition-opacity duration-700 ${
            isComplete ? 'opacity-80' : 'opacity-0'
          }`}
          style={isComplete ? { boxShadow: `0 0 32px ${roleColor('proposer')}77` } : undefined}
        />

        {!isComplete && (
          <div
            className="absolute inset-0 rounded-full border-2 border-dashed"
            style={{ borderColor: roleColor('proposer') + '60', animation: 'spin 14s linear infinite' }}
          />
        )}

        <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} className="transform -rotate-90" style={{ position: 'absolute', inset: 0 }}>
          {/* Track - segmented circle with gaps */}
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke="var(--pr-border)"
            strokeWidth="6"
            strokeDasharray={trackDashArray}
          />
          {/* Progress - single growing dash that fills segments sequentially */}
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke={phaseColor}
            strokeWidth="6"
            strokeDasharray={progressDashArray}
            strokeDashoffset={0}
            strokeLinecap="round"
            style={{ transition: 'stroke-dasharray 0.5s ease-out, stroke 0.4s', filter: `drop-shadow(0 0 4px ${phaseColor}66)` }}
          />
        </svg>

        <div className="relative flex flex-col items-center justify-center text-center select-none px-2">
          {isComplete ? (
            <span
              className="font-serif font-bold leading-tight text-center"
              style={{
                fontSize: '10px',
                letterSpacing: '0.08em',
                color: roleColor('proposer'),
                textShadow: `0 0 8px ${roleColor('proposer')}88`,
                maxWidth: `${radius * 1.25}px`,
                wordBreak: 'break-word',
                lineHeight: 1.35,
              }}
            >
              EQUI<br />LIBRIUM
            </span>
          ) : (
            <span
              className="font-mono tracking-wider font-bold"
              style={{
                fontSize: '10px',
                color: phaseColor,
                maxWidth: `${radius * 1.3}px`,
                wordBreak: 'break-word',
                lineHeight: 1.3,
                textAlign: 'center',
              }}
            >
              {phaseLabel}
            </span>
          )}
        </div>
      </div>

      <span className="text-[10px] font-mono mt-2 select-none" style={{ color: 'var(--pr-text-muted)' }}>
        {isComplete ? 'Round complete' : `${currentTurn} / ${totalTurns} turns`}
      </span>
    </div>
  );
};
