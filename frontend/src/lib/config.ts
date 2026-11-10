/**
 * Centralised frontend configuration.
 *
 * `/api/frontend-config` is the only source. There is no hard-coded fallback: the app
 * boot awaits this request and shows a fatal error if it is missing or incomplete.
 * appsettings.json -> FrontendConfig is therefore the single deployment source.
 */

// -- Types ------------------------------------------------------------------

export interface FrontendConfig {
  // Dialectical parameters
  maxLevels: number
  completionThreshold: number

  // API client
  defaultTimeout: number
  defaultRetries: number
  retryBackoffBaseMs: number

  // Notifications
  maxNotifications: number
  toastDismissMs: number

  // Persistence
  debouncePersistMs: number

  // Replay
  replaySpeeds: number[]
  replayBaseDelayMs: number

  // Cycle dial
  defaultCycleTotalTurns: number

  // Timing
  chainDelayMs: number
  savedIndicatorMs: number
  verifyChainIndicatorMs: number
  copiedIndicatorMs: number
  onboardingDelayMs: number

  // Fit-view / zoom
  fitViewAllPadding: number
  fitViewAllDuration: number
  fitViewLevelPadding: number
  fitViewLevelDuration: number
  fitViewShortcutPadding: number
  fitViewShortcutDuration: number
  zoomDuration: number

  // Cosmic backdrop
  backdropStars: number
  backdropTargetFps: number
  backdropTimeStep: number

  // Layout - helical
  layoutHelicalBaseRadius: number
  layoutHelicalInterjectionSpacing: number
  layoutHelicalLevelGap: number
  layoutHelicalStackOffset: number

  // Layout - cluster / dialectical
  layoutClusterQuestionRadius: number
  layoutClusterDebateRadius: number
  layoutClusterVerticalOffset: number
  layoutClusterChildGap: number
  layoutDuplicateSpacing: number
  layoutLevelRotation: number
  layoutRingSpacing: number
  layoutRingMinRadius: number

  // Cycle dial geometry
  cycleDialSize: number
  cycleDialRadius: number

  // Settings defaults
  defaultQualityThreshold: number
  defaultMaxDepth: number
  defaultAutoArchiveDays: number
  defaultRateLimit: number

  // Metadata
  version: string

  // Agent list
  agents: string[]
}

const REQUIRED_KEYS = [
  'maxLevels', 'completionThreshold',
  'defaultTimeout', 'defaultRetries', 'retryBackoffBaseMs',
  'maxNotifications', 'toastDismissMs', 'debouncePersistMs',
  'replaySpeeds', 'replayBaseDelayMs', 'defaultCycleTotalTurns',
  'chainDelayMs', 'savedIndicatorMs', 'verifyChainIndicatorMs',
  'copiedIndicatorMs', 'onboardingDelayMs',
  'fitViewAllPadding', 'fitViewAllDuration',
  'fitViewLevelPadding', 'fitViewLevelDuration',
  'fitViewShortcutPadding', 'fitViewShortcutDuration', 'zoomDuration',
  'backdropStars', 'backdropTargetFps', 'backdropTimeStep',
  'layoutHelicalBaseRadius', 'layoutHelicalInterjectionSpacing',
  'layoutHelicalLevelGap', 'layoutHelicalStackOffset',
  'layoutClusterQuestionRadius', 'layoutClusterDebateRadius',
  'layoutClusterVerticalOffset', 'layoutClusterChildGap',
  'layoutDuplicateSpacing', 'layoutLevelRotation',
  'layoutRingSpacing', 'layoutRingMinRadius',
  'cycleDialSize', 'cycleDialRadius',
  'defaultQualityThreshold', 'defaultMaxDepth',
  'defaultAutoArchiveDays', 'defaultRateLimit',
  'version', 'agents',
] as const satisfies readonly (keyof FrontendConfig)[]

// -- Singleton state --------------------------------------------------------

let _config: FrontendConfig | null = null
let _loading: Promise<FrontendConfig> | null = null

/**
 * Fetches /api/frontend-config. Throws if the endpoint fails or any required key is
 * missing. Safe to call multiple times - the request is only made once.
 */
export async function loadFrontendConfig(): Promise<FrontendConfig> {
  if (_config) return _config
  if (_loading) return _loading

  _loading = (async () => {
    const res = await fetch('/api/frontend-config')
    if (!res.ok) {
      throw new Error(`Failed to load frontend config: HTTP ${res.status} ${res.statusText}`.trim())
    }

    const raw = await res.json() as Record<string, unknown>
    const missing = REQUIRED_KEYS.filter((key) => raw[key] === undefined || raw[key] === null)
    if (missing.length > 0) {
      throw new Error(`Frontend config is incomplete. Missing: ${missing.join(', ')}.`)
    }

    if (!Array.isArray(raw.agents) || raw.agents.length === 0) {
      throw new Error('Frontend config agents list is missing or empty.')
    }

    _config = raw as unknown as FrontendConfig
    return _config
  })()

  return _loading
}

/** Returns the loaded config. Throws before `loadFrontendConfig()` has resolved. */
export function getConfig(): FrontendConfig {
  if (!_config) {
    throw new Error('Frontend config has not been loaded. Call loadFrontendConfig() before rendering.')
  }
  return _config
}

/**
 * Test-only: installs a known config without the boot HTTP request. Production code must
 * use `loadFrontendConfig()`; this is never called by the app.
 */
export function setFrontendConfigForTests(config: FrontendConfig): void {
  _config = config
}
