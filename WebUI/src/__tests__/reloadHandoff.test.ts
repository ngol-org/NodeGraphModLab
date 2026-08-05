import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { writeHandoff, consumeHandoff } from '../lib/reloadHandoff'
import type { NodeGraphData } from '../types/protocol'

const STORAGE_KEY = 'ngol_reload_handoff'

function makeGraph(overrides: Partial<NodeGraphData> = {}): NodeGraphData {
  return {
    id: 'graph-1',
    name: 'Test Graph',
    description: '',
    schemaVersion: 2,
    version: 1,
    createdAt: new Date().toISOString(),
    nodes: [],
    connections: [],
    fragments: [],
    fragmentLinks: [],
    groups: [],
    annotations: [],
    ...overrides,
  } as NodeGraphData
}

describe('reloadHandoff', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('書いた状態をそのまま取り出せる', () => {
    const graph = makeGraph({ name: 'My Graph' })
    expect(writeHandoff({ graph })).toBe(true)

    const restored = consumeHandoff()
    expect(restored?.graph.id).toBe('graph-1')
    expect(restored?.graph.name).toBe('My Graph')
  })

  it('viewport も往復する', () => {
    writeHandoff({ graph: makeGraph(), viewport: { x: 12, y: -34, zoom: 1.5 } })
    expect(consumeHandoff()?.viewport).toEqual({ x: 12, y: -34, zoom: 1.5 })
  })

  it('viewport 未指定なら復元側も undefined', () => {
    writeHandoff({ graph: makeGraph() })
    expect(consumeHandoff()?.viewport).toBeUndefined()
  })

  it('consume は1回だけ成功する（2回目は null）', () => {
    writeHandoff({ graph: makeGraph() })
    expect(consumeHandoff()).not.toBeNull()
    expect(consumeHandoff()).toBeNull()
  })

  it('consume すると sessionStorage から消える', () => {
    writeHandoff({ graph: makeGraph() })
    consumeHandoff()
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('何も書かれていなければ null', () => {
    expect(consumeHandoff()).toBeNull()
  })

  it('期限切れ（60秒超）は破棄する', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-05T00:00:00Z'))
    writeHandoff({ graph: makeGraph() })

    vi.setSystemTime(new Date('2026-08-05T00:01:01Z')) // +61 秒
    expect(consumeHandoff()).toBeNull()
  })

  it('期限内（60秒以内）なら復元する', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-05T00:00:00Z'))
    writeHandoff({ graph: makeGraph() })

    vi.setSystemTime(new Date('2026-08-05T00:00:59Z'))
    expect(consumeHandoff()).not.toBeNull()
  })

  it('version が違う payload は破棄する', () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      version: 999,
      savedAt: Date.now(),
      graph: makeGraph(),
    }))
    expect(consumeHandoff()).toBeNull()
  })

  it('JSON として壊れていれば破棄する', () => {
    sessionStorage.setItem(STORAGE_KEY, '{ not json')
    expect(consumeHandoff()).toBeNull()
  })

  it('graph の形が不正なら破棄する', () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      version: 1,
      savedAt: Date.now(),
      graph: { id: 'x' }, // nodes が無い
    }))
    expect(consumeHandoff()).toBeNull()
  })

  it('壊れた payload でも領域は消える（次回に持ち越さない）', () => {
    sessionStorage.setItem(STORAGE_KEY, '{ not json')
    consumeHandoff()
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('書き込みに失敗したら false を返す', () => {
    const spy = vi.spyOn(sessionStorage, 'setItem').mockImplementation(() => {
      throw new DOMException('quota', 'QuotaExceededError')
    })
    expect(writeHandoff({ graph: makeGraph() })).toBe(false)
    spy.mockRestore()
  })
})
