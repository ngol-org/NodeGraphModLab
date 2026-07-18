import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import {
  registerNodeTypeOverride,
  resolveNodeTypeOverride,
  setNodeTypeOverrideDisabled,
  subscribeNodeTypeOverrides,
  getNodeTypeOverrideSnapshot,
  __resetNodeTypeOverridesForTest,
  type NodeTypeOverrideEntry,
} from '../webuiPlugin/nodeTypeOverrideRegistry'

const DummyComponent = () => null

function makeEntry(label: string, kind: NodeTypeOverrideEntry['kind'] = 'node'): NodeTypeOverrideEntry {
  return { kind, component: DummyComponent, options: {}, label }
}

describe('nodeTypeOverrideRegistry', () => {
  beforeEach(() => {
    __resetNodeTypeOverridesForTest()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('T126-UT-01: 登録したエントリを resolve で取得できる', () => {
    registerNodeTypeOverride('ngol.logic.log', makeEntry('my.pack'))

    const resolved = resolveNodeTypeOverride(getNodeTypeOverrideSnapshot(), 'ngol.logic.log')

    expect(resolved).not.toBeNull()
    expect(resolved!.label).toBe('my.pack')
    expect(resolved!.kind).toBe('node')
  })

  it('T126-UT-02: 同一 nodeTypeId への再登録は後勝ちで console.warn が出る (G4)', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    registerNodeTypeOverride('ngol.logic.log', makeEntry('pack-a'))
    registerNodeTypeOverride('ngol.logic.log', makeEntry('pack-b'))

    const resolved = resolveNodeTypeOverride(getNodeTypeOverrideSnapshot(), 'ngol.logic.log')

    expect(resolved!.label).toBe('pack-b')
    expect(warnSpy).toHaveBeenCalledTimes(1)
    expect(warnSpy.mock.calls[0][0]).toContain('pack-a')
    expect(warnSpy.mock.calls[0][0]).toContain('pack-b')
  })

  it('T126-UT-03: disabled にした型は resolve が null (G2)、再有効化で復活', () => {
    registerNodeTypeOverride('ngol.logic.log', makeEntry('my.pack'))

    setNodeTypeOverrideDisabled('ngol.logic.log', true)
    expect(resolveNodeTypeOverride(getNodeTypeOverrideSnapshot(), 'ngol.logic.log')).toBeNull()

    setNodeTypeOverrideDisabled('ngol.logic.log', false)
    expect(resolveNodeTypeOverride(getNodeTypeOverrideSnapshot(), 'ngol.logic.log')).not.toBeNull()
  })

  it('T126-UT-04: 未登録の nodeTypeId は null（安全）', () => {
    expect(resolveNodeTypeOverride(getNodeTypeOverrideSnapshot(), 'ngol.unknown.node')).toBeNull()
  })

  it('登録・トグルで購読リスナーに通知され、スナップショット参照が差し替わる', () => {
    const listener = vi.fn()
    subscribeNodeTypeOverrides(listener)

    const snap0 = getNodeTypeOverrideSnapshot()
    registerNodeTypeOverride('ngol.logic.log', makeEntry('my.pack'))
    const snap1 = getNodeTypeOverrideSnapshot()
    setNodeTypeOverrideDisabled('ngol.logic.log', true)
    const snap2 = getNodeTypeOverrideSnapshot()

    expect(listener).toHaveBeenCalledTimes(2)
    expect(snap1).not.toBe(snap0)
    expect(snap2).not.toBe(snap1)
  })

  it('同一値への setNodeTypeOverrideDisabled は通知しない（無駄な再描画防止）', () => {
    registerNodeTypeOverride('ngol.logic.log', makeEntry('my.pack'))
    const listener = vi.fn()
    subscribeNodeTypeOverrides(listener)

    setNodeTypeOverrideDisabled('ngol.logic.log', false)

    expect(listener).not.toHaveBeenCalled()
  })
})
