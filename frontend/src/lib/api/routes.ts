/**
 * Centralised API route constants.
 *
 * Every HTTP path used by the frontend lives here.  If the backend renames an
 * endpoint, only this file needs to change.
 */

export const ROUTES = {
  // Health
  health: '/api/health',

  // Auth
  authValidate: '/api/auth/validate',
  authStreamToken: '/api/auth/stream-token',

  // Sessions
  sessions: '/api/sessions',
  deleteAllSessions: '/api/sessions/all',
  sessionById: (id: string) => `/api/sessions/${encodeURIComponent(id)}`,
  sessionHistory: (id: string) => `/api/sessions/${encodeURIComponent(id)}/history`,
  sessionReplay: (id: string) => `/api/sessions/${encodeURIComponent(id)}/replay`,
  sessionClaims: (id: string) => `/api/sessions/${encodeURIComponent(id)}/claims`,
  sessionExport: (id: string) => `/api/sessions/${encodeURIComponent(id)}/export`,
  sessionExportReport: (id: string) => `/api/sessions/${encodeURIComponent(id)}/export/report`,
  sessionVerify: (id: string) => `/api/sessions/${encodeURIComponent(id)}/verify`,

  // Nodes
  nodes: '/api/nodes',

  // Dialectical ascent
  ascend: '/api/ascend',
  ascendStream: '/api/ascend/stream',

  // Philosophy dialectics
  philosophyAscend: '/api/dialectics/philosophy/ascend',
  philosophyStream: (sessionId: string) =>
    `/api/dialectics/philosophy/ascend/stream?sessionId=${encodeURIComponent(sessionId)}`,

  // Modules
  dialecticsModules: '/api/dialectics/modules',
  modules: '/api/modules',
  moduleByKey: (key: string) => `/api/modules/${encodeURIComponent(key)}`,
  dialecticsConfig: '/api/dialectics/config',

  // Sigil
  sigil: (cid: string) => `/api/sigil/${encodeURIComponent(cid)}`,

  // Ingestion
  ingestionCycle: '/api/ingestion/cycle',
  ingestionSource: '/api/ingestion/source',
  ingestionAudit: '/api/ingestion/audit',
  ingestionFalsify: '/api/ingestion/falsify',
  ingestionDocuments: '/api/ingestion/documents',
  ingestionCitations: '/api/ingestion/citations',

  // Frontend config
  frontendConfig: '/api/frontend-config',


  // Admin
  adminData: '/api/admin/data',
  adminServerStatus: '/api/admin/server-status',
  adminLaunchServers: '/api/admin/launch-servers',
  adminLaunchServer: (key: string) => `/api/admin/launch-servers/${key}`,
  adminStopServers: '/api/admin/stop-servers',
  adminStopServer: (key: string) => `/api/admin/stop-servers/${key}`,
} as const
