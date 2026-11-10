import type { RawApiNode } from '../../layout';
import { findRootQuestion } from '../../lib/transcript';
import { classifyNodeType } from '../../layout';
import {
  isCompletedSynthesisNodeType,
  SYNTHESIS_NODE_TYPE_TO_SPEAKER,
  speakerLabel,
} from '../../config/agentRegistry';

/**
 * Marks a question as a fractal-echo return when it follows a sustained synthesis and
 * asks the session root again. The echo event itself is not persisted, so this derivation
 * keeps the return marker visible after a reload.
 */
export function annotateFractalEchoNodes(apiData: RawApiNode[]): RawApiNode[] {
  const rootQuestion = findRootQuestion(apiData);
  if (!rootQuestion) return apiData;

  const root = rootQuestion.trim().toLowerCase();
  const sustainedLevels = new Set(
    apiData
      .filter((item) =>
        item.isSustained === true && isCompletedSynthesisNodeType(item.nodeType))
      .map((item) => item.level)
  );
  if (sustainedLevels.size === 0) return apiData;

  return apiData.map((item) => {
    if (!item.content || classifyNodeType(item.nodeType) !== 'question') return item;
    const level = item.level ?? 0;
    if (!sustainedLevels.has(level - 1)) return item;
    return item.content.trim().toLowerCase() === root
      ? { ...item, isEcho: true }
      : item;
  });
}

export function resolveSynthesisInfo(
  displaySpeaker?: string,
  activeModule?: string,
): { speaker: string; speakerId: string; nodeType: string } {
  if (displaySpeaker && displaySpeaker.trim()) {
    const raw = displaySpeaker.trim().toLowerCase();
    const nodeType = SYNTHESIS_NODE_TYPE_TO_SPEAKER[raw] || raw;
    return {
      // The synthesis role is the Moderator, while SpeakerId stays the agent (mediator)
      // so stored provenance is unchanged.
      speaker: speakerLabel(raw, nodeType, 'synthesis'),
      speakerId: raw,
      nodeType,
    };
  }
  switch (activeModule?.toLowerCase()) {
    case 'research':
    case 'philosophy':
    default:
      return { speaker: 'Moderator', speakerId: 'mediator', nodeType: 'mediator' };
  }
}
