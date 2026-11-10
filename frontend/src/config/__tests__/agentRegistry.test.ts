import { describe, expect, it } from 'vitest';
import {
  AGENT_ICON_MAP,
  PHILOSOPHY_AGENTS,
  classifyAgentRole,
  speakerLabel,
} from '../agentRegistry';

describe('speakerLabel', () => {
  it('renders synthesis as Moderator and interjection as Mediator', () => {
    expect(speakerLabel('mediator', 'mediator_synthesis', 'synthesis')).toBe('Moderator');
    expect(speakerLabel('mediator', 'mediator_interjection', 'mediation')).toBe('Mediator');
    // The collapsed node type carries only the role, so the role must decide.
    expect(speakerLabel('mediator', 'mediator', 'synthesis')).toBe('Moderator');
    expect(speakerLabel('mediator', 'mediator', 'mediation')).toBe('Mediator');
  });

  it('keeps explicit identities and dialectician labels', () => {
    expect(speakerLabel('proposer', 'proposer_reply', 'thesis')).toBe('Proposer');
    expect(speakerLabel('skeptic', 'skeptic_reply', 'antithesis')).toBe('Skeptic');
    expect(speakerLabel('', 'question', 'thesis')).toBe('Question');
    expect(speakerLabel('echo', 'fractal_echo', 'thesis')).toBe('Echo');
    expect(speakerLabel('', 'system', 'synthesis')).toBe('System');
    expect(speakerLabel('', 'gate', 'gate')).toBe('Gate');
  });
});

describe('classifyAgentRole', () => {
  it('keeps the Mediator agent in the mediation role', () => {
    expect(classifyAgentRole('mediator')).toBe('mediation');
    expect(classifyAgentRole('proposer')).toBe('thesis');
    expect(classifyAgentRole('skeptic')).toBe('antithesis');
  });
});

describe('AGENT_ICON_MAP', () => {
  it('covers every agent across all modules', () => {
    const allAgents = [
      ...PHILOSOPHY_AGENTS,
    ];

    for (const agent of allAgents) {
      expect(AGENT_ICON_MAP, `${agent} missing from AGENT_ICON_MAP`).toHaveProperty(agent);
      expect(AGENT_ICON_MAP[agent].icon, `${agent} has no icon`).toBeDefined();
      expect(AGENT_ICON_MAP[agent].accentColorRole, `${agent} has no accentColorRole`).toBeDefined();
    }
  });
});