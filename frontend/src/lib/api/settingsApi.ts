/**
 * Typed fetch helpers for the /api/settings endpoints.
 *
 * The DB is the only runtime source. There are no client-side defaults: a missing or
 * incomplete settings payload is an error that the UI must show loudly. Appsettings and
 * the bound options are used by the first-run seeder only.
 */

// -- Engine config types --------------------------------------------------------

export interface QualityGatesConfig {
  enabled: boolean;
  maxTurns: number;
  consecutiveFarewellsThreshold: number;
  similarityThreshold: number;
  agreementStagnationTurns: number;
  ceremonyScoreWarning: number;
  verboseWordCount: number;
  contentRepetitionWarning: number;
  qualityScoreExitThreshold: number;
  lowQualityConsecutiveTurns: number;
  minTurnsForSimilarityCheck: number;
  circularRestatementThreshold: number;
  premiseForkDistanceThreshold: number;
  premiseForkSustainedTurns: number;
}

export interface DialecticsConfig {
  // Basic
  maxTurns: number;
  maxAscentLevels: number;
  stabilityThreshold: number;
  stabilityWindow: number;
  defaultIntensity: number;
  maxInterjections: number;
  ascentPressureEnabled: boolean;
  maxContextTurns: number;
  maxAscentContextLevels: number;
  // Word counts
  dialecticianTargetWordCount: number;
  dialecticianMaxWordCount: number;
  moderatorTargetWordCount: number;
  moderatorMaxWordCount: number;
  mediatorTargetWordCount: number;
  mediatorMaxWordCount: number;
  dialecticianDebateMaxWords: number;
  // Advanced
  noveltyThreshold: number;
  maxAscensionGateRetries: number;
  maxSynthesisRetries: number;
  synthesisResonanceThreshold: number;
  variabilityMinimum: number;
  synthesisMemoryWindow: number;
  saturationBoostIntensity: number;
  citationTopN: number;
  variabilityCrisisThreshold: number;
  crisisMinTurns: number;
  growthMaxTurns: number;
  similarityToPreviousHigh: number;
  ceremonyScoreHigh: number;
  enhancedPromptIntensityThreshold: number;
  interjectionSnippetMaxLength: number;
  defaultTensionIntensity: number;
  moderatorMessageTruncateLength: number;
  sustainedConfidenceThreshold: number;
  moderatorThemeMaxLength: number;
  moderatorConnectiveMaxLength: number;
}

// -- Types ----------------------------------------------------------------------

export type Framework = 'philosophy' | 'research';

export interface AgentConfig {
  enabled: boolean;
  qualityWeight: number;
}

export interface SettingsDto {
  framework: string;
  isConfigured: boolean;
  userName: string;

  // Connection
  provider: string;
  model: string;
  apiEndpoint: string;
  apiKey: string;
  authToken: string;
  hasApiKey: boolean;
  hasAuthToken: boolean;
  rateLimit: number;

  // Engine
  layoutMode: 'helical' | 'dialectical';
  maxDepth: number;
  qualityThreshold: number;
  enableStreamingSSE: boolean;
  persistReasoningTrace: boolean;
  useStaticQuestions: boolean;

  // Display
  fontSize: 'small' | 'medium' | 'large';
  enableAnimations: boolean;

  // Session defaults
  sessionMaxDepth: number;
  autoArchiveDays: number;
  sessionPrefix: string;

  // Export
  exportFormat: 'markdown' | 'pdf' | 'json';
  includeMerkleProofs: boolean;
  includeMetrics: boolean;
  complianceReport: boolean;

  // Notifications
  notifyOnSynthesis: boolean;
  notifyOnGateRejection: boolean;
  notifyOnSessionStall: boolean;
  notifyOnMaxDepth: boolean;
  notificationEmail: string;
  webhookUrl: string;

  // Agents (JSON string: Record<string, AgentConfig>)
  agentsJson: string;

  // Engine config (JSON blobs - stored per-framework in SQLite)
  qualityGatesJson: string;
  dialecticsJson: string;

  updatedAt?: string;
}

export interface SettingsStatusDto {
  isConfigured: boolean;
}

export interface ProviderDefault {
  model: string;
  endpoint: string;
}

// -- Validation -----------------------------------------------------------------

function parseObject(json: string, field: string): Record<string, unknown> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch (error) {
    throw new Error(`${field} is not valid JSON: ${error instanceof Error ? error.message : String(error)}`);
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`${field} must be a JSON object.`);
  }

  return parsed as Record<string, unknown>;
}

/** Throws with a complete list of missing/invalid fields. Never substitutes defaults. */
export function assertSettingsComplete(dto: SettingsDto): void {
  const missing: string[] = [];

  const requiredText: Array<keyof SettingsDto> = [
    'framework', 'provider', 'model', 'apiEndpoint', 'layoutMode', 'fontSize',
    'exportFormat', 'agentsJson', 'qualityGatesJson', 'dialecticsJson',
  ];
  for (const key of requiredText) {
    const value = dto[key];
    if (typeof value !== 'string' || !value.trim()) missing.push(String(key));
  }

  const requiredPositive: Array<keyof SettingsDto> = [
    'rateLimit', 'maxDepth', 'qualityThreshold', 'sessionMaxDepth', 'autoArchiveDays',
  ];
  for (const key of requiredPositive) {
    const value = dto[key];
    if (typeof value !== 'number' || !(value > 0)) missing.push(String(key));
  }

  if (typeof dto.qualityThreshold === 'number' && dto.qualityThreshold > 1) {
    missing.push('qualityThreshold(<=1)');
  }

  if (missing.length > 0) {
    throw new Error(
      `Settings for '${dto.framework || 'unknown'}' are incomplete or invalid: ${missing.join(', ')}. ` +
      'Fix or re-seed the settings row; the application will not substitute defaults.',
    );
  }

  const agents = parseObject(dto.agentsJson, 'agentsJson');
  if (Object.keys(agents).length === 0) {
    throw new Error(`agentsJson for '${dto.framework}' is empty.`);
  }
  parseObject(dto.qualityGatesJson, 'qualityGatesJson');
  parseObject(dto.dialecticsJson, 'dialecticsJson');
}

/** Normalises PascalCase engine JSON from older seed rows to the camelCase shape the UI uses. */
export function normalizeConfigKeys<T extends object>(value: T): T {
  const normalised: Record<string, unknown> = {};
  for (const [key, entry] of Object.entries(value as Record<string, unknown>)) {
    const camel = key.length > 0 ? key.charAt(0).toLowerCase() + key.slice(1) : key;
    normalised[camel] = entry;
  }
  return normalised as T;
}

// -- API helpers ----------------------------------------------------------------

async function readError(res: Response): Promise<string> {
  const body = await res.json().catch(() => null) as { detail?: string; title?: string; errors?: Record<string, string[]> } | null;
  if (body?.detail) return body.detail;
  if (body?.title) return body.title;
  return `HTTP ${res.status} ${res.statusText}`.trim();
}

/** Returns { isConfigured } for the framework. */
export async function getSettingsStatus(framework: Framework): Promise<SettingsStatusDto> {
  const res = await fetch(`/api/settings/${framework}/status`);
  if (!res.ok) throw new Error(`Failed to load settings status: ${await readError(res)}`);
  return res.json() as Promise<SettingsStatusDto>;
}

/** Returns saved settings for the framework. Missing/invalid settings throw. */
export async function getSettings(framework: Framework): Promise<SettingsDto> {
  const res = await fetch(`/api/settings/${framework}`);
  if (!res.ok) throw new Error(`Failed to load settings: ${await readError(res)}`);
  const dto = await res.json() as SettingsDto;
  assertSettingsComplete(dto);
  return dto;
}

/** Creates or updates settings for the framework. */
export async function saveSettings(framework: Framework, dto: SettingsDto, source?: string): Promise<SettingsDto> {
  const res = await fetch(`/api/settings/${framework}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  });
  if (!res.ok) throw new Error(`Failed to save settings: ${await readError(res)}`);

  const result = await res.json() as SettingsDto;
  assertSettingsComplete(result);

  // Notify other components (like MindMap and GraphViewSwitcher) that settings changed.
  // `source` lets the saving hook ignore its own echo and keep newer in-flight edits.
  window.dispatchEvent(new CustomEvent('seldonSettingsUpdated', {
    detail: { framework, settings: result, source },
  }));

  return result;
}

/** Provider catalog + deployment defaults from appsettings. */
export async function getProviderDefaults(): Promise<Record<string, ProviderDefault>> {
  const res = await fetch('/api/settings/providers');
  if (!res.ok) throw new Error(`Failed to load provider catalog: ${await readError(res)}`);

  const catalog = await res.json() as Record<string, ProviderDefault>;
  if (!catalog || Object.keys(catalog).length === 0) {
    throw new Error('Provider catalog is empty. Configure Settings:Providers in appsettings.');
  }

  for (const [provider, defaults] of Object.entries(catalog)) {
    if (!defaults || typeof defaults.model !== 'string' || typeof defaults.endpoint !== 'string') {
      throw new Error(`Provider catalog entry '${provider}' is incomplete.`);
    }
  }

  return catalog;
}
