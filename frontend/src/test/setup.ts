import { setFrontendConfigForTests, type FrontendConfig } from '../lib/config'

/**
 * Test fixture only. The production app has no fallback: it must load
 * /api/frontend-config before rendering. Tests that exercise layout/client code install
 * this known shape so they do not need a live backend.
 */
const TEST_CONFIG: FrontendConfig = {
  maxLevels: 10,
  completionThreshold: 9,
  defaultTimeout: 30_000,
  defaultRetries: 2,
  retryBackoffBaseMs: 300,
  maxNotifications: 50,
  toastDismissMs: 5000,
  debouncePersistMs: 300,
  replaySpeeds: [0.5, 1, 2, 4],
  replayBaseDelayMs: 1500,
  defaultCycleTotalTurns: 10,
  chainDelayMs: 1500,
  savedIndicatorMs: 1500,
  verifyChainIndicatorMs: 3000,
  copiedIndicatorMs: 2000,
  onboardingDelayMs: 500,
  fitViewAllPadding: 0.15,
  fitViewAllDuration: 600,
  fitViewLevelPadding: 0.65,
  fitViewLevelDuration: 600,
  fitViewShortcutPadding: 0.65,
  fitViewShortcutDuration: 400,
  zoomDuration: 200,
  backdropStars: 50,
  backdropTargetFps: 12,
  backdropTimeStep: 0.015,
  layoutHelicalBaseRadius: 120,
  layoutHelicalInterjectionSpacing: 60,
  layoutHelicalLevelGap: 150,
  layoutHelicalStackOffset: 100,
  layoutClusterQuestionRadius: 110,
  layoutClusterDebateRadius: 100,
  layoutClusterVerticalOffset: 70,
  layoutClusterChildGap: 300,
  layoutDuplicateSpacing: 45,
  layoutLevelRotation: 0.8,
  layoutRingSpacing: 90,
  layoutRingMinRadius: 110,
  cycleDialSize: 128,
  cycleDialRadius: 50,
  defaultQualityThreshold: 0.7,
  defaultMaxDepth: 10,
  defaultAutoArchiveDays: 30,
  defaultRateLimit: 100,
  version: 'test',
  agents: ['proposer', 'skeptic', 'mediator'],
}

setFrontendConfigForTests(TEST_CONFIG)
