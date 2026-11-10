/**
 * Round selection → ascent level mapping.
 *
 * Two different axes are in play here and must not be conflated:
 *
 *  - the graph's *view selection* - which round the user is looking at, and
 *  - the *ascent level* a session actually runs at.
 *
 * Round pills are 1-based over ascent levels, so "Round N" is level N-1.
 */

export type RoundSelection = number | 'all' | null;

/**
 * The ascent level a newly created session begins at.
 *
 * A new session always begins at its root. `activeLevel` is a view filter, not a
 * start level: inheriting it here started fresh sessions at whatever round the
 * previously viewed session had reached.
 */
export const NEW_SESSION_START_LEVEL = 0;

/** Selection used when there is no user choice, or the previous one no longer applies. */
export const DEFAULT_ROUND_SELECTION: RoundSelection = 'all';

/** Maps the view selection to the level the graph focuses on. */
export function focusLevelForSelection(
  selection: RoundSelection,
): number | 'all' | null {
  if (selection === 'all' || selection === null) return selection;
  return selection - 1;
}

/**
 * Selection to carry across a module switch.
 *
 * Round numbers are framework-specific, so a selection made in one module is
 * reset when the user switches to another.
 */
export function selectionForModuleChange(
  previousModule: string | null | undefined,
  nextModule: string | null | undefined,
  current: RoundSelection,
): RoundSelection {
  return previousModule === nextModule ? current : DEFAULT_ROUND_SELECTION;
}
