import type { XYPosition } from 'reactflow';
import { getConfig } from '../lib/config';

export interface QuestionCluster {
  question: XYPosition;
  proposer: XYPosition;
  skeptic: XYPosition;
  mediator: XYPosition;
  childQuestion: XYPosition;
}

/**
 * Every question becomes a small "solar system": the question sits at the
 * centre with its debate replies arranged around it at a fixed,
 * consistent geometry. This is what keeps every debate cluster looking
 * identical no matter where it lands on the ring.
 */
export class ClusterLayout {
  readonly questionRadius = getConfig().layoutClusterQuestionRadius;
  readonly debateRadius = getConfig().layoutClusterDebateRadius;

  positionQuestion(x: number, y: number): QuestionCluster {
    const r = this.debateRadius;
    return {
      question: { x, y },
      proposer: { x, y: y - r },
      skeptic: { x: x - r, y: y + getConfig().layoutClusterVerticalOffset },
      mediator: { x: x + r, y: y + getConfig().layoutClusterVerticalOffset },
      childQuestion: { x, y: y + getConfig().layoutClusterChildGap },
    };
  }
}
