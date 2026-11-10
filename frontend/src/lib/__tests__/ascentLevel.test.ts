import { describe, it, expect } from 'vitest';
import {
  NEW_SESSION_START_LEVEL,
  DEFAULT_ROUND_SELECTION,
  focusLevelForSelection,
  selectionForModuleChange,
} from '../ascentLevel';

describe('ascent level mapping', () => {
  it('a new session always starts at the root level', () => {
    expect(NEW_SESSION_START_LEVEL).toBe(0);
  });

  it('rounds are 1-based over ascent levels', () => {
    expect(focusLevelForSelection(1)).toBe(0);
    expect(focusLevelForSelection(10)).toBe(9);
  });

  it('passes through the no-choice selections', () => {
    expect(focusLevelForSelection('all')).toBe('all');
    expect(focusLevelForSelection(null)).toBeNull();
  });

  it('carries a selection only within the same module', () => {
    expect(selectionForModuleChange('philosophy', 'philosophy', 10)).toBe(10);
    expect(selectionForModuleChange('philosophy', 'research', 10)).toBe(DEFAULT_ROUND_SELECTION);
    expect(selectionForModuleChange('research', 'philosophy', 3)).toBe(DEFAULT_ROUND_SELECTION);
  });

  it('a stale round cannot leak into a new module run', () => {
    // Regression: take a session to level 9 (leaving "Round 10" selected),
    // switch module, then submit a new session.
    const carried = selectionForModuleChange('philosophy', 'research', 10);

    // The selection does not carry across the switch...
    expect(carried).toBe(DEFAULT_ROUND_SELECTION);
    // ... and the new session starts at the root regardless.
    expect(NEW_SESSION_START_LEVEL).toBe(0);
  });
});
