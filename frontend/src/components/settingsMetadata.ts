export type SectionId =
  | 'servers'
  | 'connection'
  | 'engine'
  | 'quality-gates'
  | 'dialectics'
  | 'agents'
  | 'display'
  | 'notifications'
  | 'export'
  | 'sessions'
  | 'audit'
  | 'data';

export interface LayerGroupSpec {
  id: string;
  name: string;
  subtitle: string;
  agents: string[];
  colour: string;
}

export const PHILOSOPHY_LAYER_GROUPS: LayerGroupSpec[] = [
  {
    id: 'philosophy-dialectic',
    name: 'Philosophical Dialectic',
    subtitle: 'Proposer (Thesis) · Skeptic (Antithesis) · Mediator (Mediation & Moderation)',
    agents: ['proposer', 'skeptic', 'mediator'],
    colour: '#11b880',
  },
];
