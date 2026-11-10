import { getConfig } from '../lib/config';

/**
 * Computes the radius for a ring of `questionCount` nodes at a given level,
 * sized so nodes never crowd each other regardless of how many questions
 * end up sharing a level. Replaces the old fixed `250 + level * 250`
 * formula, which broke down as soon as a level held more than a handful
 * of nodes.
 */
export class RingAllocator {
  private readonly spacing = getConfig().layoutRingSpacing;
  private readonly minimumRadius = getConfig().layoutRingMinRadius;

  constructor() {}

  allocate(questionCount: number, level: number): number {
    const circumference = Math.max(questionCount, 1) * this.spacing;
    const required = circumference / (2 * Math.PI);
    return Math.max(this.minimumRadius * level, required);
  }
}
