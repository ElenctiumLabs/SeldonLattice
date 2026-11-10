import type { RawApiNode, DialecticRoleType } from '../../layout';
import type { LiveTurn } from '../../types/liveTurn';
import type { MindMapStreamData } from './mindMapStreamData';

interface DialecticianStreamRecordInput {
  data: MindMapStreamData;
  id: string;
  eventType: string;
  speaker: string;
  content: string;
  role: LiveTurn['dialecticRole'];
  level: number;
  turn: number;
  timestamp: number;
}

export function createDialecticianStreamRecords({
  data,
  id,
  eventType,
  speaker,
  content,
  role,
  level,
  turn,
  timestamp,
}: DialecticianStreamRecordInput): { liveTurn: LiveTurn; pendingNode: RawApiNode } {
  const metrics = (
    data.qualityScore != null ||
    data.ceremonyScore != null ||
    data.ascentScore != null ||
    data.joinMediation != null ||
    data.joinNovelty != null ||
    data.irreducibleTension != null ||
    data.isSustained === true
  )
    ? {
        qualityScore: data.qualityScore ?? undefined,
        ceremonyScore: data.ceremonyScore ?? undefined,
        similarityScore: data.similarityToPrevious ?? undefined,
        ascentScore: data.ascentScore ?? undefined,
        joinMediation: data.joinMediation ?? undefined,
        joinNovelty: data.joinNovelty ?? undefined,
        irreducibleTension: data.irreducibleTension ?? undefined,
        isSustained: data.isSustained === true ? true : undefined,
      }
    : undefined;

  const nodeDialecticRole: DialecticRoleType =
    role === 'thesis' || role === 'antithesis' || role === 'synthesis' || role === 'interjection'
      ? role
      : 'antithesis';

  return {
    liveTurn: {
      id,
      speaker,
      content,
      dialecticRole: role,
      level,
      timestamp,
      metrics,
      phase: data.phase,
      isSustained: data.isSustained,
      reasoningTrace: data.reasoningTrace,
      remainingDissensus: data.remainingDissensus,
      repeatsPremise: data.repeatsPremise,
      unresolvedTensions: data.unresolvedTensions,
      tensionIntensity: data.tensionIntensity,
    },
    pendingNode: {
      cid: id,
      nodeType: eventType,
      level,
      content,
      dialecticRole: nodeDialecticRole,
      speakerId: speaker.toLowerCase(),
      cyclePhase: data.phase,
      turn,
      isPending: true,
    },
  };
}
