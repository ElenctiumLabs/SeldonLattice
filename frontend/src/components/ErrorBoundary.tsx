import { Component, type ErrorInfo, type ReactNode } from 'react'

interface ErrorBoundaryProps {
  children: ReactNode
  fallback?: ReactNode
  /** Optional label for the error report */
  label?: string
  /** Called when an error is caught */
  onError?: (error: Error, errorInfo: ErrorInfo) => void
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
  errorInfo: ErrorInfo | null
  retryCount: number
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null, errorInfo: null, retryCount: 0 }
  }

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    this.setState({ errorInfo })
    console.error(`[ErrorBoundary${this.props.label ? `: ${this.props.label}` : ''}]`, error, errorInfo)
    this.props.onError?.(error, errorInfo)
  }

  handleRetry = () => {
    this.setState((prev) => ({
      hasError: false,
      error: null,
      errorInfo: null,
      retryCount: prev.retryCount + 1,
    }))
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback

      return (
        <div className="flex flex-col items-center justify-center h-full min-h-[200px] p-6 bg-[#080a0f] text-[#f1f5f9]">
          <div className="max-w-md text-center">
            <div className="w-12 h-12 mx-auto mb-4 rounded-full bg-[#5a2a1a] flex items-center justify-center">
              <svg className="w-6 h-6 text-[#d4886a]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="12" cy="12" r="10" />
                <path d="M12 8v4M12 16h.01" />
              </svg>
            </div>
            <h3 className="text-[14px] font-mono uppercase tracking-widest text-[#f1f5f9] mb-2">
              Something went wrong
            </h3>
            <p className="text-[13px] text-[#8b9bb4] mb-4 font-mono">
              {this.state.error?.message || 'An unexpected error occurred'}
            </p>
            <div className="flex items-center justify-center gap-3">
              <button
                onClick={this.handleRetry}
                className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer"
                style={{
                  backgroundColor: '#fbbf24',
                  color: '#080a0f',
                }}
              >
                Try Again
              </button>
              <button
                onClick={() => window.location.reload()}
                className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer border border-[rgba(255,255,255,0.08)] text-[#8b9bb4] hover:text-[#f1f5f9]"
              >
                Reload Page
              </button>
            </div>
            {this.state.retryCount > 0 && (
              <p className="mt-3 text-[11px] font-mono text-[#5a6a82]">
                Retried {this.state.retryCount} time{this.state.retryCount > 1 ? 's' : ''}
              </p>
            )}
          </div>
        </div>
      )
    }

    return this.props.children
  }
}

export default ErrorBoundary
