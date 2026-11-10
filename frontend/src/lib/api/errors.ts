export class ApiError extends Error {
  status: number
  statusText: string
  body?: unknown

  constructor(status: number, statusText: string, body?: unknown) {
    super(`API error ${status}: ${statusText}`)
    this.name = 'ApiError'
    this.status = status
    this.statusText = statusText
    this.body = body
  }
}

export class NetworkError extends Error {
  cause?: Error

  constructor(cause?: Error) {
    super('Network error: could not reach the API')
    this.name = 'NetworkError'
    this.cause = cause
  }
}

export class TimeoutError extends Error {
  timeoutMs: number

  constructor(timeoutMs: number) {
    super(`Request timed out after ${timeoutMs}ms`)
    this.name = 'TimeoutError'
    this.timeoutMs = timeoutMs
  }
}
