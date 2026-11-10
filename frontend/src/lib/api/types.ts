// ---- Session ------------------------------------------------------------------

export type SessionStatus = 'active' | 'archived'

export interface Session {
  sessionId: string
  framework: string
  rootQuestion: string
  createdAt: string
  lastUpdatedAt: string | null
  nodeCount: number
  maxLevel: number
  status: SessionStatus
}

export interface CreateSessionRequest {
  rootQuestion: string
  framework?: string
}

export interface ListSessionsQuery {
  framework?: string
  status?: SessionStatus
}

// ---- Nodes --------------------------------------------------------------------

export type CyclePhase = 'emergence' | 'growth' | 'crisis' | 'saturation' | 'renewal'

export interface NodeDto {
  cid: string
  nodeType: string
  level: number
  parentHash: string | null
  parentHashes: string[] | null
  content: string
  cyclePhase: CyclePhase
  variabilityScore: number
  resonanceScore: number
  isSecondaryCenter: boolean
  centerShiftScore: number
  sessionId: string
  speakerId: string
  moderatorSelectionConfidence: number
  timestamp: string
  parentPatternHash: string | null
  ascentScore: number
  joinMediation: number | null
  joinNovelty: number | null
  irreducibleTension: number
  reasoningTrace: string | null
  remainingDissensus: string | null
  repeatsPremise: number | null
  unresolvedTensions: string[] | null
  tensionIntensity: number | null
  isSustained: boolean
  // Per-turn response quality metrics. Null = the quality gates did not score this
  // node (question and synthesis nodes); 0 is a genuine measured zero.
  qualityScore: number | null
  ceremonyScore: number | null
  similarityToPrevious: number | null
  newArgumentCount: number | null
  wordCount: number | null
  hasFarewell: boolean | null
  hasAgreement: boolean | null
  hasDisagreement: boolean | null
  selfSimilarity: number | null
}

export interface PagedResponse<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface NodesQuery {
  sessionId?: string
  level?: number
  page?: number
  pageSize?: number
}

// ---- Dialectical Ascent ------------------------------------------------------

export interface AscendRequest {
  level: number
  theme: string
  sessionId?: string
  module?: string
  rootTheme?: string
}

export interface DialogueLevelResult {
  level: number
  synthesisHash: string
  metaphor: string
  nextTheme: string
  connectiveSentence: string
  ascensionValidated: boolean
  ascensionAttempts: number
  synthesisThesis: string
  moderatorSelectionConfidence: number
  isSustained: boolean
}

// ---- Session History ----------------------------------------------------------

export interface HistoryTurn {
  turnNumber: number
  speaker: string
  role: 'question' | 'thesis' | 'antithesis' | 'synthesis' | 'mediation'
  content: string
  timestamp: string
  /** Gate score (0–100). Null when the quality gates did not score this turn. */
  qualityScore: number | null
  level: number
  ascentScore: number
  joinMediation: number | null
  joinNovelty: number | null
  irreducibleTension: number
  isSustained: boolean
  reasoningTrace: string | null
  remainingDissensus: string | null
  repeatsPremise: number | null
  unresolvedTensions: string[] | null
  tensionIntensity: number | null
  ceremonyScore: number | null
  similarityToPrevious: number | null
  newArgumentCount: number | null
  wordCount: number | null
  hasFarewell: boolean | null
  hasAgreement: boolean | null
  hasDisagreement: boolean | null
  selfSimilarity: number | null
}

export interface SessionHistory {
  sessionId: string
  rootQuestion: string
  turns: HistoryTurn[]
}

export interface SessionReplay {
  sessionId: string
  rootQuestion: string
  module: string
  turns: HistoryTurn[]
  totalTurns: number
}

export interface SessionClaim {
  level: number
  claim: string
  confidence: number
  falsifiedBy?: string
  supportedBy?: string[]
  attacks?: string[]
  synthesisHash?: string
  timestamp?: string
}

export interface SessionClaimsResponse {
  sessionId: string
  rootQuestion: string
  totalClaims: number
  claims: SessionClaim[]
}

// ---- Health ------------------------------------------------------------------

export interface DependencyHealth {
  name: string
  status: string
  detail?: string
}

export interface HealthResponse {
  status: 'healthy' | 'degraded' | 'unhealthy'
  uptime: string
  version: string
  environment: string
  dependencies: DependencyHealth[]
}

// ---- Auth --------------------------------------------------------------------

export interface AuthValidateResponse {
  authenticated: boolean
  apiKey: string
  roles: string[]
  isAdmin: boolean
}

// ---- Modules ------------------------------------------------------------------

export interface ModuleInfo {
  key: string
  displayName: string
  description: string
}

export interface AgentDefinition {
  name: string
  framework?: string
  description?: string
  style?: string
  expertise?: string[]
}

export interface MediatorConfig {
  mediatorNames?: string[]
  mode?: string
}

export interface ModeratorConfig {
  moderatorName?: string
  selectionStrategy?: string
}

export interface ModuleDefinition {
  key: string
  name: string
  description?: string
  agents: AgentDefinition[]
  qualityGates?: string[]
  mediatorConfig?: MediatorConfig
  moderatorConfig?: ModeratorConfig
}

export interface ModuleDetail {
  key: string
  name: string
  description: string
  isCustom: boolean
  agents?: AgentDefinition[]
  qualityGates?: string[]
  mediatorConfig?: MediatorConfig
  moderatorConfig?: ModeratorConfig
}

export interface ModuleCreateResponse {
  key: string
  name: string
  agentCount: number
  qualityGateCount: number
}

// ---- Server Launch Control ----------------------------------------------------

export interface ServerInfo {
  running: boolean
  starting?: boolean
  port: number
}

export interface ServerStatus {
  chat: ServerInfo
  embeddings: ServerInfo
  allRunning: boolean
}

export interface LaunchResult {
  launched: string[]
  starting?: string[]
  alreadyRunning: string[]
  errors: string[]
}

export interface StopResult {
  stopped: string[]
  errors: string[]
}

// ---- Dialectics Config --------------------------------------------------------

export interface QualityGateConfig {
  enabled: boolean
  maxTurns: number
  similarityThreshold: number
  qualityScoreExitThreshold: number
}

export interface DialecticsConfigResponse {
  modules: ModuleInfo[]
  maxAscentLevels: number
  maxTurns: number
  defaultIntensity: number
  qualityGates: QualityGateConfig
}

// ---- Sigil --------------------------------------------------------------------

export type SigilAlgorithm =
  | 'lissajous'
  | 'mandala'
  | 'fractal_tree'
  | 'all'

export interface SigilDto {
  algorithm: string
  cid: string
  svg: string
  size: number
}

export interface SigilJsonResponse {
  cid: string
  size: number
  algorithm?: string
  sigil?: SigilDto
  sigils?: SigilDto[]
}

export interface SigilQuery {
  algorithm?: SigilAlgorithm
  size?: number
  format?: 'svg' | 'json'
}

// ---- Ingestion ----------------------------------------------------------------

export interface IngestSourceRequest {
  sourceId: string
  query: string
  maxResults?: number
}

export interface IngestedDocument {
  cid: string
  sourceId: string
  title: string
  culture: string
  domain: string
  sourceTimestamp: string
  ingestedAt: string
  bodyPreview: string
}

// ---- SSE Events --------------------------------------------------------------

export type DialecticRole = 'thesis' | 'antithesis' | 'synthesis' | 'interjection' | 'mediation'

export interface SseEvent {
  Type: string
  SpeakerId?: string
  Content?: string
  Metaphor?: string
  NextTheme?: string
  ExitReason?: string
  TriggeringSignal?: string
  Turn?: number
  QualityScore?: number
  CeremonyScore?: number
  SimilarityToPrevious?: number
  Phase?: string
  ConnectiveSentence?: string
  AscensionValidated?: boolean
  SynthesisThesis?: string
  IsSustained?: boolean
}

// ---- Export --------------------------------------------------------------------

export type ExportFormat = 'transcript' | 'markdown' | 'json' | 'report'

export interface VerificationFinding {
  nodeType: string
  nodeCid: string
  severity: 'Error' | 'Warning'
  code: string
  message: string
}

export interface SessionVerificationResult {
  sessionId: string
  verified: boolean
  nodesChecked: number
  signaturesVerified: number
  pointerIssues?: number
  signatureIssues?: number
  contentIssues?: number
  unreadableBlobs?: number
  sessionRoot?: string
  rootMatches?: boolean | null
  issues?: VerificationFinding[]
}

export interface RawApiNode {
  cid: string
  nodeType: string
  level: number
  content: string
  cyclePhase?: string
  parentHash?: string
  parentHashes?: string[]
  speakerId?: string
  dialecticRole?: DialecticRole
  turn?: number
  isSustained?: boolean
  timestamp?: string
  sessionId?: string
  // Negative-dialectic markers (present on synthesis nodes).
  remainingDissensus?: string | null
  repeatsPremise?: number | null
  unresolvedTensions?: string[] | null
  tensionIntensity?: number | null
  ascentScore?: number
  joinMediation?: number | null
  joinNovelty?: number | null
  irreducibleTension?: number
}

