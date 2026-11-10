import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  SeldonClient,
  loadStoredApiKey,
} from '../client'

describe('SeldonClient.setApiKey', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      text: async () => '{"ok":true}',
    })
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('constructs with the key from config', async () => {
    const client = new SeldonClient({ baseUrl: 'http://test.local', apiKey: 'cfg-key' })
    await client.getHealth()
    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers).toEqual({ Authorization: 'ApiKey cfg-key' })
  })

  it('sends no Authorization header when no key is set', async () => {
    const client = new SeldonClient({ baseUrl: 'http://test.local' })
    await client.getHealth()
    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers).toEqual({})
  })

  it('carries the Authorization header after setApiKey', async () => {
    const client = new SeldonClient({ baseUrl: 'http://test.local' })
    client.setApiKey('live-key')
    await client.getHealth()
    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers).toEqual({ Authorization: 'ApiKey live-key' })
  })

  it('updates subsequent requests when the key changes', async () => {
    const client = new SeldonClient({ baseUrl: 'http://test.local' })
    client.setApiKey('first-key')
    await client.getHealth()
    client.setApiKey('second-key')
    await client.getHealth()
    const first = fetchMock.mock.calls[0][1].headers
    const second = fetchMock.mock.calls[1][1].headers
    expect(first).toEqual({ Authorization: 'ApiKey first-key' })
    expect(second).toEqual({ Authorization: 'ApiKey second-key' })
  })

  it('getApiKey returns the current key', () => {
    const client = new SeldonClient()
    expect(client.getApiKey()).toBe('')
    client.setApiKey('abc')
    expect(client.getApiKey()).toBe('abc')
  })
})

describe('loadStoredApiKey', () => {
  const store = new Map<string, string>()

  beforeEach(() => {
    store.clear()
    vi.stubGlobal('localStorage', {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => store.set(k, v),
      removeItem: (k: string) => store.delete(k),
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns empty string when no settings are stored', () => {
    expect(loadStoredApiKey()).toBe('')
  })

  it('returns the stored apiKeys.apiKey', () => {
    store.set(
      'seldon-settings',
      JSON.stringify({ apiKeys: { apiKey: 'stored-key', authToken: 'tok', rateLimit: 50 } }),
    )
    expect(loadStoredApiKey()).toBe('stored-key')
  })

  it('returns empty string when apiKeys.apiKey is missing', () => {
    store.set('seldon-settings', JSON.stringify({ provider: 'OpenCodeZen' }))
    expect(loadStoredApiKey()).toBe('')
  })

  it('returns empty string on malformed JSON', () => {
    store.set('seldon-settings', '{not valid json')
    expect(loadStoredApiKey()).toBe('')
  })
})
