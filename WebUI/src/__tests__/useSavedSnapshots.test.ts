import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { ServerMessage } from '../types/protocol'

// wsClient をモック化し、onMessage に登録されたコールバックをテストから直接発火できるようにする
let messageHandlers: Array<(msg: ServerMessage) => void> = []
vi.mock('../lib/wsClient', () => ({
  wsClient: {
    onMessage: (cb: (msg: ServerMessage) => void) => {
      messageHandlers.push(cb)
      return () => {
        messageHandlers = messageHandlers.filter(h => h !== cb)
      }
    },
    getSnapshotHistory: vi.fn(),
    restoreSnapshot: vi.fn(),
    clearSnapshot: vi.fn(),
  },
}))

function emit(msg: ServerMessage) {
  act(() => {
    messageHandlers.forEach(h => h(msg))
  })
}

// モック後にインポート（vi.mock はホイストされるため import 順は問題ない）
import { useSavedSnapshots, useSnapshotHistory, snapshotPortKey } from '../hooks/useGraphEditor'

describe('snapshotPortKey', () => {
  it('nodeInstanceId と portName を : で結合する', () => {
    expect(snapshotPortKey('node-1', 'items')).toBe('node-1:items')
  })
})

describe('useSavedSnapshots', () => {
  beforeEach(() => {
    messageHandlers = []
  })

  it('複数ポートに順番に snapshot_saved しても savedSnapshotsByPort は全ポート分残る', () => {
    const { result } = renderHook(() => useSavedSnapshots())

    // ListItemSelectorNode 相当: items → selected → index の順で SetSnapshot
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'items', valueType: 'String', valueString: '["a","b"]' })
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'selected', valueType: 'String', valueString: 'a' })
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'index', valueType: 'Int32', valueString: '0' })

    // 新設: ポート別マップは3件とも残っている（従来の不具合はここが1件に潰れていた）
    expect(result.current.savedSnapshotsByPort.get('n1:items')?.valueString).toBe('["a","b"]')
    expect(result.current.savedSnapshotsByPort.get('n1:selected')?.valueString).toBe('a')
    expect(result.current.savedSnapshotsByPort.get('n1:index')?.valueString).toBe('0')

    // 後方互換: 単一バッジは最後に書いたポート（index）のみ
    expect(result.current.savedSnapshots.get('n1')?.portName).toBe('index')
  })

  it('all_snapshots_cleared で両方の Map が空になる', () => {
    const { result } = renderHook(() => useSavedSnapshots())

    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'items', valueType: 'String', valueString: '[]' })
    emit({ type: 'all_snapshots_cleared' })

    expect(result.current.savedSnapshots.size).toBe(0)
    expect(result.current.savedSnapshotsByPort.size).toBe(0)
  })

  it('snapshot_store_state で再接続時に両方の Map が同期される', () => {
    const { result } = renderHook(() => useSavedSnapshots())

    emit({
      type: 'snapshot_store_state',
      entries: [
        { nodeInstanceId: 'n1', portName: 'items', valueType: 'String', valueString: '["a"]' },
        { nodeInstanceId: 'n1', portName: 'selected', valueType: 'String', valueString: 'a' },
        { nodeInstanceId: 'n2', portName: 'value', valueType: 'Double', valueString: '1.5' },
      ],
    })

    expect(result.current.savedSnapshotsByPort.get('n1:items')?.valueString).toBe('["a"]')
    expect(result.current.savedSnapshotsByPort.get('n1:selected')?.valueString).toBe('a')
    expect(result.current.savedSnapshotsByPort.get('n2:value')?.valueString).toBe('1.5')
  })
})

describe('useSnapshotHistory の snapshot_cleared', () => {
  beforeEach(() => {
    messageHandlers = []
  })

  it('portName 指定ありのクリアは該当ポートのみ savedSnapshotsByPort から削除する', () => {
    const { result: snapResult } = renderHook(() => useSavedSnapshots())
    const { result: histResult } = renderHook(() =>
      useSnapshotHistory(snapResult.current.savedSnapshots, snapResult.current.setSavedSnapshots, snapResult.current.setSavedSnapshotsByPort)
    )
    void histResult // フック登録のためだけに使用（onMessage購読を有効化）

    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'items', valueType: 'String', valueString: '["a"]' })
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'selected', valueType: 'String', valueString: 'a' })
    emit({ type: 'snapshot_cleared', nodeInstanceId: 'n1', portName: 'selected' })

    expect(snapResult.current.savedSnapshotsByPort.has('n1:items')).toBe(true)
    expect(snapResult.current.savedSnapshotsByPort.has('n1:selected')).toBe(false)
  })

  it('portName 省略（全ポートクリア）は該当ノードの全ポートを savedSnapshotsByPort から削除する', () => {
    const { result: snapResult } = renderHook(() => useSavedSnapshots())
    const { result: histResult } = renderHook(() =>
      useSnapshotHistory(snapResult.current.savedSnapshots, snapResult.current.setSavedSnapshots, snapResult.current.setSavedSnapshotsByPort)
    )
    void histResult

    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'items', valueType: 'String', valueString: '["a"]' })
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n1', portName: 'selected', valueType: 'String', valueString: 'a' })
    emit({ type: 'snapshot_saved', nodeInstanceId: 'n2', portName: 'value', valueType: 'Double', valueString: '1.5' })
    // ClearSnapshotHandler.cs はポート省略時 portName: "" で push する
    emit({ type: 'snapshot_cleared', nodeInstanceId: 'n1', portName: '' })

    expect(snapResult.current.savedSnapshotsByPort.has('n1:items')).toBe(false)
    expect(snapResult.current.savedSnapshotsByPort.has('n1:selected')).toBe(false)
    // 別ノードの分は影響を受けない
    expect(snapResult.current.savedSnapshotsByPort.has('n2:value')).toBe(true)
  })
})
