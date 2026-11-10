import React, { createContext, useContext, useEffect, useState, useCallback, useRef } from 'react'
import { api } from '../lib/api'
import type { ServerStatus, LaunchResult, StopResult } from '../lib/api'

interface ServerStatusContextValue {
  status: ServerStatus | null;
  loading: boolean;
  launching: boolean;
  stopping: boolean;
  launchingKey: string | null;
  stoppingKey: string | null;
  error: string | null;
  launchResult: LaunchResult | null;
  stopResult: StopResult | null;
  refresh: () => Promise<void>;
  launch: () => Promise<void>;
  stop: () => Promise<void>;
  launchOne: (key: string) => Promise<void>;
  stopOne: (key: string) => Promise<void>;
  /** True when all servers are running - engine actions are enabled. */
  engineEnabled: boolean;
}

const ServerStatusContext = createContext<ServerStatusContextValue | null>(null)

const POLL_INTERVAL_MS = 60_000 // 60 seconds

export function ServerStatusProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<ServerStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [launching, setLaunching] = useState(false);
  const [stopping, setStopping] = useState(false);
  const [launchingKey, setLaunchingKey] = useState<string | null>(null);
  const [stoppingKey, setStoppingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null)
  const [launchResult, setLaunchResult] = useState<LaunchResult | null>(null)
  const [stopResult, setStopResult] = useState<StopResult | null>(null)
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const mountedRef = useRef(true)

  const refresh = useCallback(async () => {
    try {
      const s = await api.getServerStatus()
      if (mountedRef.current) {
        setStatus(s)
        setError(null)
      }
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Failed to fetch server status')
      }
    } finally {
      if (mountedRef.current) {
        setLoading(false)
      }
    }
  }, [])

  const launch = useCallback(async () => {
    setLaunching(true)
    setLaunchResult(null)
    try {
      const result = await api.launchServers()
      if (mountedRef.current) {
        setLaunchResult(result)
      }
      // Wait a moment for servers to start, then refresh
      await new Promise((r) => setTimeout(r, 30_000))
      await refresh()
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Failed to launch servers')
      }
    } finally {
      if (mountedRef.current) {
        setLaunching(false)
      }
    }
  }, [refresh])

  const stop = useCallback(async () => {
    setStopping(true)
    setStopResult(null)
    try {
      const result = await api.stopServers()
      if (mountedRef.current) {
        setStopResult(result)
      }
      await refresh()
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Failed to stop servers')
      }
    } finally {
      if (mountedRef.current) {
        setStopping(false)
      }
    }
  }, [refresh])

  // Poll every 60s
  useEffect(() => {
    mountedRef.current = true
    refresh()
    pollingRef.current = setInterval(refresh, POLL_INTERVAL_MS)
    return () => {
      mountedRef.current = false
      if (pollingRef.current) {
        clearInterval(pollingRef.current)
      }
    }
  }, [refresh])

  // Auto-launch on mount if servers are down
  useEffect(() => {
    if (status && !status.allRunning && !launching) {
      launch()
    }
  }, [status, launching, launch])

  const launchOne = useCallback(async (key: string) => {
    setLaunchingKey(key)
    try {
      const result = await api.launchServer(key)
      if (mountedRef.current) {
        setLaunchResult(result)
      }
      await new Promise((r) => setTimeout(r, 1500))
      await refresh()
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Failed to launch server')
      }
    } finally {
      if (mountedRef.current) {
        setLaunchingKey(null)
      }
    }
  }, [refresh])

  const stopOne = useCallback(async (key: string) => {
    setStoppingKey(key)
    try {
      const result = await api.stopServer(key)
      if (mountedRef.current) {
        setStopResult(result)
      }
      await refresh()
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Failed to stop server')
      }
    } finally {
      if (mountedRef.current) {
        setStoppingKey(null)
      }
    }
  }, [refresh])

  const engineEnabled = status?.allRunning ?? false

  return (
    <ServerStatusContext.Provider
      value={{
        status,
        loading,
        launching,
        stopping,
        launchingKey,
        stoppingKey,
        error,
        launchResult,
        stopResult,
        refresh,
        launch,
        stop,
        launchOne,
        stopOne,
        engineEnabled,
      }}
    >
      {children}
    </ServerStatusContext.Provider>
  )
}

export function useServerStatus(): ServerStatusContextValue {
  const ctx = useContext(ServerStatusContext)
  if (!ctx) {
    throw new Error('useServerStatus must be used within ServerStatusProvider')
  }
  return ctx
}

/** Convenience hook - returns true when all servers are running. */
export function useEngineEnabled(): boolean {
  return useServerStatus().engineEnabled
}
