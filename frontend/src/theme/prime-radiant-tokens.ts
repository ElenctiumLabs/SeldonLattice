/**
 * Prime Radiant - theme tokens (TS mirror)
 * React Flow reads node/edge styles from JS objects, not the DOM, so
 * they can't resolve CSS custom properties. This file is the
 * literal-value twin of prime-radiant-tokens.css.
 *
 * Palette: The Great Work / Hermetic design system (matches seldonlattice.com)
 */

export type DialecticalRole = 'question' | 'proposer' | 'skeptic' | 'judge' | 'mediation';

export const primeRadiantDark = {
  surface: {
    bg: '#080a0f',
    surface: '#11151c',
    surfaceRaised: '#161b26',
    border: 'rgba(255, 255, 255, 0.08)',
    borderStrong: 'rgba(255, 255, 255, 0.15)',
  },
  role: {
    question: { base: '#b05a3a', soft: '#d4886a' },
    proposer: { base: '#fbbf24', soft: '#e8d48a' },
    skeptic: { base: '#8a7358', soft: '#b7a483' },
    judge: { base: '#10b981', soft: '#34d399' },
    // Generic mediation (fallback)
    mediation: { base: '#8b5cf6', soft: '#c4b5fd' },
  } satisfies Record<DialecticalRole, { base: string; soft: string }>,
  text: {
    primary: '#f1f5f9',
    muted: '#9bb0cb',
    dim: '#768ca8',
  },
  font: {
    display: "'Cormorant Garamond', 'Cardo', serif",
    displaySc: "'Cormorant SC', 'Cormorant Garamond', serif",
    body: "'EB Garamond', serif",
    mono: "'Fira Code', monospace",
  },
} as const;

export const primeRadiantLight = {
  surface: {
    bg: '#f8f6f0',
    surface: '#ffffff',
    surfaceRaised: '#fcfbf8',
    border: 'rgba(30, 25, 18, 0.14)',
    borderStrong: 'rgba(30, 25, 18, 0.28)',
  },
  role: {
    question: { base: '#8c3e20', soft: '#a8502d' },
    proposer: { base: '#78591a', soft: '#8f6c24' },
    skeptic: { base: '#5c4933', soft: '#7c654b' },
    judge: { base: '#046340', soft: '#06704d' },
    // Generic mediation (fallback)
    mediation: { base: '#6d28d9', soft: '#8b5cf6' },
  } satisfies Record<DialecticalRole, { base: string; soft: string }>,
  text: {
    primary: '#0f0c08',
    muted: '#2c241b',
    dim: '#4e4235',
  },
  font: {
    display: "'Cormorant Garamond', 'Cardo', serif",
    displaySc: "'Cormorant SC', 'Cormorant Garamond', serif",
    body: "'EB Garamond', serif",
    mono: "'Fira Code', monospace",
  },
} as const;

export const primeRadiant = primeRadiantDark;

export function isLightTheme(): boolean {
  if (typeof document === 'undefined') return false;
  const t = document.documentElement.getAttribute('data-theme');
  return t === 'light' || t === 'sol' || t === 'albedo';
}

/** Map any speaker identity to a dialectical role for coloring. */
export function roleOf(speaker: string): DialecticalRole {
  const map: Record<string, DialecticalRole> = {
    question: 'question',
    user: 'question',
    theme: 'question',
    mediator: 'judge',
    proposer: 'proposer',
    skeptic: 'skeptic',
    judge: 'judge',

  };
  return map[speaker.toLowerCase()] ?? 'proposer';
}

export function roleColor(role: DialecticalRole, variant: 'base' | 'soft' = 'base'): string {
  const themeTokens = isLightTheme() ? primeRadiantLight : primeRadiantDark;
  return themeTokens.role[role]?.[variant] ?? themeTokens.role.proposer[variant];
}

export function messageRoleClass(speaker: string): string {
  if (speaker === 'system') return 'pr-message';
  return `pr-message pr-message--${roleOf(speaker)}`;
}

