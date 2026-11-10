import type { RawApiNode } from '../../layout';
import { findQuestionForLevel } from '../../lib/transcript';

export interface MindMapResumePoint {
  resumeLevel: number;
  resumeTheme: string;
  currentTheme: string;
  canResume: boolean;
}

/** Resolve the next stream level and its theme from a session's persisted graph. */
export function resolveMindMapResumePoint(
  apiData: RawApiNode[],
  rootQuestion: string | undefined,
  maxLevel: number | undefined,
  maxLevels: number,
): MindMapResumePoint {
  const synthesisLevels = new Set(
    apiData
      .filter((node) => {
        const nodeType = (node.nodeType ?? '').toLowerCase();
        return nodeType.includes('synthesis') || nodeType.includes('socrat');
      })
      .map((node) => node.level ?? 0)
  );
  const highestSynthesisLevel = synthesisLevels.size > 0 ? Math.max(...synthesisLevels) : -1;
  const resumeLevel = highestSynthesisLevel + 1;
  const resumeTheme = findQuestionForLevel(apiData, resumeLevel) ?? rootQuestion ?? '';
  const canResume = resumeLevel < maxLevels;

  return {
    resumeLevel,
    resumeTheme,
    canResume,
    currentTheme: canResume
      ? resumeTheme
      : findQuestionForLevel(apiData, maxLevel ?? 0) ?? rootQuestion ?? '',
  };
}
