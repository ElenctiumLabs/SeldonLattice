import { ApiError, NetworkError, TimeoutError } from './errors'
import { ROUTES } from './routes'
import { getConfig } from '../config'
import type {
  AscendRequest,
  AuthValidateResponse,
  CreateSessionRequest,
  DialogueLevelResult,
  DialecticsConfigResponse,
  ExportFormat,
  LaunchResult,
  ServerStatus,
  StopResult,
  HealthResponse,
  IngestedDocument,
  IngestSourceRequest,
  ListSessionsQuery,
  ModuleCreateResponse,
  ModuleDefinition,
  ModuleDetail,
  ModuleInfo,
  NodeDto,
  NodesQuery,
  PagedResponse,
  RawApiNode,
  Session,
  SessionHistory,
  SessionReplay,
  SessionClaimsResponse,
  SigilJsonResponse,
  SigilQuery,
  SseEvent, 
  SessionVerificationResult,
} from './types'

export interface ClientConfig {
  baseUrl?: string
  apiKey?: string
  timeout?: number
  /** Number of retries for network/timeout errors (default: 2) */
  retries?: number
}

export class SeldonClient {
  private readonly baseUrl: string
  private apiKey: string
  private readonly timeout: number
  private readonly retries: number

  constructor(config: ClientConfig = {}) {
    const cfg = getConfig()
    this.baseUrl = config.baseUrl ?? ''
    this.apiKey = config.apiKey ?? ''
    this.timeout = config.timeout ?? cfg.defaultTimeout
    this.retries = config.retries ?? cfg.defaultRetries
  }

  /** Updates the API key used for authenticated requests without re-instantiating the client. */
  setApiKey(key: string) {
    this.apiKey = key
  }

  /** Returns the currently configured API key (empty string if unset). */
  getApiKey(): string {
    return this.apiKey
  }

  // -- Core fetch wrapper ----------------------------------------------------

  private authHeader(): Record<string, string> {
    return this.apiKey ? { Authorization: `ApiKey ${this.apiKey}` } : {}
  }

  private async request<T>(
    method: string,
    path: string,
    options?: {
      body?: unknown
      query?: Record<string, string | number | boolean | undefined | null>
      signal?: AbortSignal
    },
  ): Promise<T> {
    let lastError: unknown
    const cfg = getConfig()

    for (let attempt = 0; attempt <= this.retries; attempt++) {
      if (attempt > 0) {
        await new Promise((r) => setTimeout(r, cfg.retryBackoffBaseMs * Math.pow(2, attempt - 1)))
      }

      const url = new URL(path, this.baseUrl || window.location.origin)

      if (options?.query) {
        for (const [key, value] of Object.entries(options.query)) {
          if (value != null && value !== '') {
            url.searchParams.set(key, String(value))
          }
        }
      }

      const controller = new AbortController()
      const timer = setTimeout(() => controller.abort(), this.timeout)
      const signal = options?.signal
        ? anySignal([options.signal, controller.signal])
        : controller.signal

      try {
        const headers: Record<string, string> = {
          ...this.authHeader(),
        }
        if (options?.body) {
          headers['Content-Type'] = 'application/json'
        }

        const res = await fetch(url.toString(), {
          method,
          headers,
          body: options?.body ? JSON.stringify(options.body) : undefined,
          signal,
        })

        if (!res.ok) {
          let body: unknown
          try {
            body = await res.json()
          } catch {
            body = await res.text()
          }
          // Don't retry client errors (4xx) - only server errors (5xx)
          if (res.status >= 400 && res.status < 500) {
            throw new ApiError(res.status, res.statusText, body)
          }
          if (attempt < this.retries) {
            lastError = new ApiError(res.status, res.statusText, body)
            continue
          }
          throw new ApiError(res.status, res.statusText, body)
        }

        const text = await res.text()
        if (!text) return undefined as T
        return JSON.parse(text) as T
      } catch (err) {
        if (err instanceof ApiError) throw err
        if (err instanceof DOMException && err.name === 'AbortError') {
          if (options?.signal?.aborted) throw err
          if (attempt < this.retries) {
            lastError = new TimeoutError(this.timeout)
            continue
          }
          throw new TimeoutError(this.timeout)
        }
        // Network error - retry
        if (attempt < this.retries) {
          lastError = new NetworkError(err instanceof Error ? err : undefined)
          continue
        }
        throw new NetworkError(err instanceof Error ? err : undefined)
      } finally {
        clearTimeout(timer)
      }
    }

    // Should not reach here, but just in case
    throw lastError instanceof Error ? lastError : new Error('Request failed')
  }

  // -- Health ----------------------------------------------------------------

  async getHealth(signal?: AbortSignal): Promise<HealthResponse> {
    return this.request<HealthResponse>('GET', ROUTES.health, { signal })
  }

  // -- Auth ------------------------------------------------------------------

  async validateAuth(signal?: AbortSignal): Promise<AuthValidateResponse> {
    return this.request<AuthValidateResponse>('GET', ROUTES.authValidate, {
      signal,
    })
  }

  async getStreamToken(
    sessionId: string,
    module?: string,
    signal?: AbortSignal,
  ): Promise<{ streamToken: string }> {
    return this.request<{ streamToken: string }>(
      'POST',
      ROUTES.authStreamToken,
      {
        body: { sessionId, module: module ?? 'philosophy' },
        signal,
      },
    )
  }

  // -- Sessions --------------------------------------------------------------

  async createSession(
    rootQuestion: string,
    framework?: string,
    signal?: AbortSignal,
  ): Promise<{ sessionId: string }> {
    return this.request<{ sessionId: string }>('POST', ROUTES.sessions, {
      body: { rootQuestion, framework } satisfies CreateSessionRequest,
      signal,
    })
  }

  async listSessions(
    query: ListSessionsQuery = {},
    signal?: AbortSignal,
  ): Promise<Session[]> {
    return this.request<Session[]>('GET', ROUTES.sessions, {
      query: query as Record<string, string>,
      signal,
    })
  }

  async getSession(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<Session> {
    return this.request<Session>('GET', ROUTES.sessionById(sessionId), {
      signal,
    })
  }

  async deleteSession(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<void> {
    return this.request<void>('DELETE', ROUTES.sessionById(sessionId), {
      signal,
    })
  }

  async deleteAllSessions(
    signal?: AbortSignal,
  ): Promise<{ deleted: number; timestamp: string }> {
    return this.request<{ deleted: number; timestamp: string }>(
      'DELETE',
      ROUTES.deleteAllSessions,
      { signal },
    )
  }

  async getSessionHistory(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<SessionHistory> {
    return this.request<SessionHistory>(
      'GET',
      ROUTES.sessionHistory(sessionId),
      { signal },
    )
  }

  async getSessionReplay(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<SessionReplay> {
    return this.request<SessionReplay>(
      'GET',
      ROUTES.sessionReplay(sessionId),
      { signal },
    )
  }

  async getSessionClaims(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<SessionClaimsResponse> {
    return this.request<SessionClaimsResponse>(
      'GET',
      ROUTES.sessionClaims(sessionId),
      { signal },
    )
  }

  // -- Nodes ------------------------------------------------------------------

  async getNodes(
    query: NodesQuery = {},
    signal?: AbortSignal,
  ): Promise<PagedResponse<NodeDto>> {
    return this.request<PagedResponse<NodeDto>>('GET', ROUTES.nodes, {
      query: query as Record<string, string | number>,
      signal,
    })
  }

  /**
   * Fetches dialectical nodes, transparently paging through the entire result set.
   *
   * GET /api/nodes clamps `pageSize` to 200 and falls back to 50 when the caller
   * omits it, so a single unpaged request returned only the first ~5 rounds of a
   * 10-level session - and because the backend orders by timestamp, the cut landed
   * mid-round. Callers that need the whole session must page until the reported
   * total is collected; `getNodes` remains available for deliberate single-page reads.
   *
   * `query.pageSize` is the page size used while collecting, not a cap on the result.
   */
  async getNodesFlat(
    query: NodesQuery = {},
    signal?: AbortSignal,
  ): Promise<RawApiNode[]> {
    const pageSize = Math.min(Math.max(query.pageSize ?? 200, 1), 200)
    const collected: NodeDto[] = []
    let page = 1
    for (;;) {
      const res = await this.getNodes({ ...query, page, pageSize }, signal)
      collected.push(...res.items)
      const totalPages = res.totalPages ?? 1
      if (page >= totalPages || res.items.length === 0) break
      page += 1
    }
    return collected.map((n) => ({
      ...n,
      parentHash: n.parentHash ?? undefined,
      parentHashes: n.parentHashes ?? undefined,
    }))
  }

  // -- Dialectical Ascent ----------------------------------------------------

  async ascend(
    request: AscendRequest,
    signal?: AbortSignal,
  ): Promise<DialogueLevelResult> {
    return this.request<DialogueLevelResult>('POST', ROUTES.ascend, {
      body: request,
      signal,
    })
  }

  // -- SSE Streaming ----------------------------------------------------------

  async streamAscent(
    params: {
      sessionId: string
      level: number
      theme: string
      module?: string
    },
    onEvent: (event: SseEvent) => void,
    onError?: (err: Event) => void,
  ): Promise<EventSource> {
    const base = this.baseUrl || window.location.origin
    const qs = new URLSearchParams({
      level: String(params.level),
      theme: params.theme,
    })

    let url: string
    if (params.module && params.module !== 'philosophy') {
      url = `${base}${ROUTES.ascendStream}?${qs}&sessionId=${params.sessionId}&module=${encodeURIComponent(params.module)}`
    } else {
      qs.set('sessionId', params.sessionId)
      url = `${base}${ROUTES.ascendStream}?${qs}`
    }

    const moduleForToken = params.module ?? 'philosophy'
    if (moduleForToken) {
      try {
        const { streamToken } = await this.getStreamToken(
          params.sessionId,
          moduleForToken,
        )
        url = `${url}${url.includes('?') ? '&' : '?'}streamToken=${encodeURIComponent(streamToken)}`
      } catch {
        // Token issuance failed - attempt unauthenticated connection
      }
    }

    const es = new EventSource(url)
    es.onmessage = (ev) => {
      try {
        onEvent(JSON.parse(ev.data) as SseEvent)
      } catch {
        // ignore parse errors
      }
    }
    if (onError) es.onerror = onError
    return es
  }

  // -- Philosophy Dialectics --------------------------------------------------

  async philosophyAscend(
    sessionId: string,
    level: number,
    theme: string,
    signal?: AbortSignal,
  ): Promise<DialogueLevelResult> {
    return this.request<DialogueLevelResult>(
      'POST',
      ROUTES.philosophyAscend,
      {
        query: { sessionId, level, theme },
        signal,
      },
    )
  }

  async listDialecticsModules(
    signal?: AbortSignal,
  ): Promise<ModuleInfo[]> {
    return this.request<ModuleInfo[]>('GET', ROUTES.dialecticsModules, {
      signal,
    })
  }

  // -- Modules CRUD ----------------------------------------------------------

  async listModules(signal?: AbortSignal): Promise<ModuleDetail[]> {
    return this.request<ModuleDetail[]>('GET', ROUTES.modules, { signal })
  }

  async getModule(
    key: string,
    signal?: AbortSignal,
  ): Promise<ModuleDetail> {
    return this.request<ModuleDetail>('GET', ROUTES.moduleByKey(key), {
      signal,
    })
  }

  async createModule(
    definition: ModuleDefinition,
    signal?: AbortSignal,
  ): Promise<ModuleCreateResponse> {
    return this.request<ModuleCreateResponse>('POST', ROUTES.modules, {
      body: definition,
      signal,
    })
  }

  // -- Dialectics Config ------------------------------------------------------

  async getDialecticsConfig(
    framework: string,
    signal?: AbortSignal,
  ): Promise<DialecticsConfigResponse> {
    return this.request<DialecticsConfigResponse>(
      'GET',
      `${ROUTES.dialecticsConfig}?framework=${encodeURIComponent(framework)}`,
      { signal },
    )
  }

  // -- Sigil ------------------------------------------------------------------

  async getSigilJson(
    cid: string,
    query: SigilQuery = {},
    signal?: AbortSignal,
  ): Promise<SigilJsonResponse> {
    return this.request<SigilJsonResponse>('GET', ROUTES.sigil(cid), {
      query: { format: 'json', ...query } as Record<string, string | number>,
      signal,
    })
  }

  getSigilUrl(cid: string, query: SigilQuery = {}): string {
    const url = new URL(
      ROUTES.sigil(cid),
      this.baseUrl || window.location.origin,
    )
    for (const [key, value] of Object.entries(query)) {
      if (value != null && value !== '') {
        url.searchParams.set(key, String(value))
      }
    }
    return url.toString()
  }

  // -- Ingestion --------------------------------------------------------------

  async runIngestionCycle(signal?: AbortSignal): Promise<unknown> {
    return this.request<unknown>('POST', ROUTES.ingestionCycle, { signal })
  }

  async ingestFromSource(
    request: IngestSourceRequest,
    signal?: AbortSignal,
  ): Promise<unknown> {
    return this.request<unknown>('POST', ROUTES.ingestionSource, {
      body: request,
      signal,
    })
  }

  async getIngestionAudit(signal?: AbortSignal): Promise<unknown> {
    return this.request<unknown>('GET', ROUTES.ingestionAudit, { signal })
  }

  async clearDataDir(
    signal?: AbortSignal,
  ): Promise<{ status: string; merkleCleared: boolean; keysDeleted: number; timestamp: string }> {
    return this.request<{ status: string; merkleCleared: boolean; keysDeleted: number; timestamp: string }>(
      'DELETE',
      ROUTES.adminData,
      { signal },
    )
  }

  async runFalsification(signal?: AbortSignal): Promise<unknown> {
    return this.request<unknown>('POST', ROUTES.ingestionFalsify, { signal })
  }

  // -- Server launch control ----------------------------------------------------

  async getServerStatus(signal?: AbortSignal): Promise<ServerStatus> {
    return this.request<ServerStatus>('GET', ROUTES.adminServerStatus, { signal })
  }

  async launchServers(signal?: AbortSignal): Promise<LaunchResult> {
    return this.request<LaunchResult>('POST', ROUTES.adminLaunchServers, { signal })
  }

  async stopServers(signal?: AbortSignal): Promise<StopResult> {
    return this.request<StopResult>('POST', ROUTES.adminStopServers, { signal })
  }

  async launchServer(key: string, signal?: AbortSignal): Promise<LaunchResult> {
    return this.request<LaunchResult>('POST', ROUTES.adminLaunchServer(key), { signal })
  }

  async stopServer(key: string, signal?: AbortSignal): Promise<StopResult> {
    return this.request<StopResult>('POST', ROUTES.adminStopServer(key), { signal })
  }

  async getIngestedDocuments(
    query: { sourceId?: string; culture?: string; domain?: string } = {},
    signal?: AbortSignal,
  ): Promise<IngestedDocument[]> {
    return this.request<IngestedDocument[]>(
      'GET',
      ROUTES.ingestionDocuments,
      {
        query: query as Record<string, string>,
        signal,
      },
    )
  }

  async exportSession(
    sessionId: string,
    format: ExportFormat = 'markdown',
    signal?: AbortSignal,
  ): Promise<Blob> {
    const url = new URL(ROUTES.sessionExport(sessionId), this.baseUrl || window.location.origin)
    url.searchParams.set('format', format)

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: this.authHeader(),
      signal,
    })

    if (!response.ok) {
      throw new ApiError(response.status, await response.text())
    }

    return response.blob()
  }

  async verifySession(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<SessionVerificationResult> {
    return this.request<SessionVerificationResult>('GET', ROUTES.sessionVerify(sessionId), {
      signal,
    })
  }

  async exportSessionReport(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<string> {
    const url = new URL(ROUTES.sessionExportReport(sessionId), this.baseUrl || window.location.origin)

    const response = await fetch(url.toString(), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.authHeader(),
      },
      signal,
    })

    if (!response.ok) {
      throw new ApiError(response.status, await response.text())
    }

    return response.text()
  }

}

// -- Helpers ----------------------------------------------------------------

function anySignal(signals: AbortSignal[]): AbortSignal {
  const controller = new AbortController()
  for (const signal of signals) {
    if (signal.aborted) {
      controller.abort(signal.reason)
      return controller.signal
    }
    signal.addEventListener('abort', () => controller.abort(signal.reason), {
      once: true,
    })
  }
  return controller.signal
}

// -- Singleton --------------------------------------------------------------

const STORAGE_KEY_SETTINGS = 'seldon-settings'

export function loadStoredApiKey(): string {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_SETTINGS)
    if (!raw) return ''
    const settings = JSON.parse(raw)
    return settings?.apiKeys?.apiKey ?? ''
  } catch {
    return ''
  }
}

export const api = new SeldonClient({
  apiKey: typeof window !== 'undefined' ? loadStoredApiKey() : '',
})
