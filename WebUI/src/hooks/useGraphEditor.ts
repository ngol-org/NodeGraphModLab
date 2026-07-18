import { useState, useEffect, useCallback, useRef, type Dispatch, type SetStateAction } from 'react'
import { wsClient } from '../lib/wsClient'
import type {
  NodeTypeInfo,
  GraphSummary,
  NodeGraphData,
  ScriptCompileErrorPush,
  SnapshotHistoryEntry,
  PersistentNodeInfo,
} from '../types/protocol'

export interface SnapshotBadgeInfo {
  portName: string
  valueType: string
  time: string
  valueString?: string | null
}

/**
 * savedSnapshotsByPort のキーを組み立てる。
 * ノードID自体はGUIDでコロンを含まないため単純な区切りで安全。
 */
export function snapshotPortKey(nodeInstanceId: string, portName: string): string {
  return `${nodeInstanceId}:${portName}`
}

// ============================================================
// 接続状態フック
// ============================================================
export function useConnection() {
  const [connected, setConnected] = useState(wsClient.isConnected)

  useEffect(() => {
    const unsub = wsClient.onConnection(setConnected)
    return unsub
  }, [])

  return connected
}

// ============================================================
// ノードタイプ一覧フック
// ============================================================
export function useNodeTypes() {
  const [nodeTypes, setNodeTypes] = useState<NodeTypeInfo[]>([])
  const connected = useConnection()

  useEffect(() => {
    // node_list_updated を複数連続受信した場合のデバウンス
    let debounceTimer: ReturnType<typeof setTimeout> | null = null
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'node_list_response') {
        setNodeTypes(msg.nodes)
      } else if (msg.type === 'node_list_updated') {
        if (debounceTimer) clearTimeout(debounceTimer)
        debounceTimer = setTimeout(() => {
          debounceTimer = null
          wsClient.getNodeList()
        }, 150)
      }
    })
    return () => {
      if (debounceTimer) clearTimeout(debounceTimer)
      unsub()
    }
  }, [])

  useEffect(() => {
    if (connected) wsClient.getNodeList()
  }, [connected])

  return nodeTypes
}

// ============================================================
// グラフ一覧フック
// ============================================================
export function useGraphList() {
  const [graphs, setGraphs] = useState<GraphSummary[]>([])
  const connected = useConnection()

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'list_graphs_response') setGraphs(msg.graphs)
    })
    return unsub
  }, [])

  const refresh = useCallback(() => wsClient.listGraphs(), [])

  useEffect(() => {
    if (connected) refresh()
  }, [connected, refresh])

  return { graphs, refresh }
}

// ============================================================
// 実行ログフック
// ============================================================
export interface LogEntry {
  localId: number
  timestampMs: number
  level: 'debug' | 'info' | 'warning' | 'error'
  message: string
  /** ログの発生源カテゴリ: exec=ノード実行, system=接続状態等, notify=サーバー通知 */
  category: 'exec' | 'system' | 'notify'
  nodeInstanceId?: string
}

let _logSeq = 0

// ============================================================
// スクリプトコンパイルエラーノティフィケーションフック (script_compile_error)
// ============================================================
export function useScriptCompileError() {
  const [compileError, setCompileError] = useState<ScriptCompileErrorPush | null>(null)

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'script_compile_error') {
        setCompileError(msg as ScriptCompileErrorPush)
      }
    })
    return unsub
  }, [])

  const dismiss = useCallback(() => setCompileError(null), [])

  return { compileError, dismiss }
}

export function useExecutionLogs(maxEntries = 200) {
  const [logs, setLogs] = useState<LogEntry[]>([])

  const addLog = useCallback((entry: Omit<LogEntry, 'localId'>) => {
    setLogs(prev => {
      const next = [...prev, { ...entry, localId: ++_logSeq }]
      return next.length > maxEntries ? next.slice(-maxEntries) : next
    })
  }, [maxEntries])

  // WebSocket メッセージ購読
  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'execution_log') {
        addLog({
          timestampMs: msg.timestampMs,
          level: msg.level,
          message: msg.message,
          category: 'exec',
          nodeInstanceId: msg.nodeInstanceId,
        })
      } else if (msg.type === 'execution_result') {
        addLog({
          timestampMs: Date.now(),
          level: msg.success ? 'info' : 'error',
          message: msg.success
            ? `\u2713 Execution completed in ${msg.durationMs.toFixed(1)}ms`
            : `\u2717 Execution failed: ${msg.errorMessage}`,
          category: 'exec',
        })
      } else if (msg.type === 'welcome') {
        addLog({
          timestampMs: Date.now(),
          level: 'info',
          message: `Plugin ${msg.pluginVersion} connected`,
          category: 'system',
        })
      } else if (msg.type === 'node_list_updated') {
        const n = msg.updatedNodeTypeIds?.length ?? 0
        addLog({
          timestampMs: Date.now(),
          level: 'info',
          message: n > 0 ? `Node list updated (${n} type${n > 1 ? 's' : ''})` : 'Node list updated',
          category: 'notify',
        })
      } else if (msg.type === 'script_compile_error') {
        addLog({
          timestampMs: Date.now(),
          level: 'error',
          message: `Compile error: ${msg.fileName} — ${msg.errorMessage}`,
          category: 'notify',
        })
      } else if (msg.type === 'export_nodes_response') {
        addLog({
          timestampMs: Date.now(),
          level: msg.success ? 'info' : 'error',
          message: msg.success
            ? `Exported: ${msg.savedPath ?? ''}`
            : `Export failed: ${msg.errorMessage ?? 'unknown error'}`,
          category: 'notify',
        })
      } else if (msg.type === 'save_graph_response') {
        addLog({
          timestampMs: Date.now(),
          level: msg.success ? 'info' : 'error',
          message: msg.success ? 'Graph saved' : 'Save failed',
          category: 'notify',
        })
      } else if (msg.type === 'load_graph_response') {
        addLog({
          timestampMs: Date.now(),
          level: msg.success ? 'info' : 'error',
          message: msg.success ? 'Graph loaded' : 'Load failed',
          category: 'notify',
        })
      }
    })
    return unsub
  }, [addLog])

  // 接続状態変化ログ
  useEffect(() => {
    const unsub = wsClient.onConnection(connected => {
      addLog({
        timestampMs: Date.now(),
        level: connected ? 'info' : 'warning',
        message: connected ? 'WebSocket connected' : 'WebSocket disconnected, reconnecting...',
        category: 'system',
      })
    })
    return unsub
  }, [addLog])

  const clear = useCallback(() => setLogs([]), [])

  return { logs, clear, addLog }
}

// ============================================================
// グラフ実行フック
// ============================================================
export function useGraphExecution() {
  const [executing, setExecuting] = useState(false)

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'execution_result') setExecuting(false)
    })
    return unsub
  }, [])

  const execute = useCallback((graph: NodeGraphData) => {
    setExecuting(true)
    wsClient.executeGraph(graph)
  }, [])

  return { executing, execute }
}

// ============================================================
// グラフ保存フック
// ============================================================
export function useGraphSave() {
  const [saving, setSaving] = useState(false)
  const [lastSaved, setLastSaved] = useState<Date | null>(null)

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'save_graph_response') {
        setSaving(false)
        if (msg.success) setLastSaved(new Date())
      }
    })
    return unsub
  }, [])

  const save = useCallback((graph: NodeGraphData) => {
    setSaving(true)
    wsClient.saveGraph(graph)
  }, [])

  return { saving, lastSaved, save }
}

// ============================================================
// ノード実行進捗フック (execution_progress)
// ノードのハイライト状態を管理する
// ============================================================
export type NodeProgressStatus = 'running' | 'done' | 'error' | 'idle'

const PROGRESS_CLEAR_DELAY_MS = 1500

export function useExecutionProgress() {
  const [nodeStatus, setNodeStatus] = useState<Record<string, NodeProgressStatus>>({})
  const timers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map())

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'execution_progress') {
        const { nodeInstanceId, status } = msg

        // 既存タイマーをクリア
        const prev = timers.current.get(nodeInstanceId)
        if (prev) clearTimeout(prev)

        setNodeStatus(prev => ({ ...prev, [nodeInstanceId]: status }))

        // done/error は一定時間後にidle に戻す
        if (status === 'done' || status === 'error') {
          const t = setTimeout(() => {
            setNodeStatus(prev => ({ ...prev, [nodeInstanceId]: 'idle' }))
            timers.current.delete(nodeInstanceId)
          }, PROGRESS_CLEAR_DELAY_MS)
          timers.current.set(nodeInstanceId, t)
        }
      } else if (msg.type === 'execution_result') {
        // 実行完了後に全ノードをidle に戻す
        const t = setTimeout(() => {
          setNodeStatus({})
          timers.current.clear()
        }, PROGRESS_CLEAR_DELAY_MS + 500)
        timers.current.set('__global__', t)
      }
    })
    return unsub
  }, [])

  // クリーンアップ
  useEffect(() => {
    return () => {
      timers.current.forEach(t => clearTimeout(t))
    }
  }, [])

  return { nodeStatus }
}

// ============================================================
// Snapshot 保存状態フック
// ============================================================
export function useSavedSnapshots() {
  // 後方互換: ノードIDのみキー、最新1件（既存の SAVED バッジ・type mismatch 判定等で使用）
  const [savedSnapshots, setSavedSnapshots] = useState<Map<string, SnapshotBadgeInfo>>(new Map())
  // `${nodeInstanceId}:${portName}` キーで全ポート分を保持（複数ポートにSetSnapshotする
  // ノード（List Item Selector 等）で先に書いたポートのバッジが後続の書き込みで消える不具合の修正）
  const [savedSnapshotsByPort, setSavedSnapshotsByPort] = useState<Map<string, SnapshotBadgeInfo>>(new Map())
  const [justSavedNodes, setJustSavedNodes] = useState<Set<string>>(new Set())
  const flashTimers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map())

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'snapshot_saved') {
        const now = new Date()
        const time = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}`
        const badge = { portName: msg.portName, valueType: msg.valueType, time, valueString: msg.valueString ?? null }
        setSavedSnapshots(prev => {
          const next = new Map(prev)
          next.set(msg.nodeInstanceId, badge)
          return next
        })
        setSavedSnapshotsByPort(prev => {
          const next = new Map(prev)
          next.set(snapshotPortKey(msg.nodeInstanceId, msg.portName), badge)
          return next
        })
        // フラッシュ: 一時的に justSavedNodes に追加 → 800ms 後に削除
        setJustSavedNodes(prev => {
          const next = new Set(prev)
          next.add(msg.nodeInstanceId)
          return next
        })
        const prevTimer = flashTimers.current.get(msg.nodeInstanceId)
        if (prevTimer) clearTimeout(prevTimer)
        const t = setTimeout(() => {
          setJustSavedNodes(prev => {
            const next = new Set(prev)
            next.delete(msg.nodeInstanceId)
            return next
          })
          flashTimers.current.delete(msg.nodeInstanceId)
        }, 800)
        flashTimers.current.set(msg.nodeInstanceId, t)
      } else if (msg.type === 'all_snapshots_cleared') {
        // 全クリア → グラフ上のSnapshotノードバッジを全消去
        setSavedSnapshots(new Map())
        setSavedSnapshotsByPort(new Map())
      } else if (msg.type === 'snapshot_store_state') {
        // 接続時/リフレッシュ時に全状態を同期
        const now = new Date()
        const time = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}`
        setSavedSnapshots(() => {
          const next = new Map<string, SnapshotBadgeInfo>()
          for (const e of msg.entries) {
            next.set(e.nodeInstanceId, { portName: e.portName, valueType: e.valueType, time, valueString: e.valueString ?? null })
          }
          return next
        })
        setSavedSnapshotsByPort(() => {
          const next = new Map<string, SnapshotBadgeInfo>()
          for (const e of msg.entries) {
            next.set(snapshotPortKey(e.nodeInstanceId, e.portName), { portName: e.portName, valueType: e.valueType, time, valueString: e.valueString ?? null })
          }
          return next
        })
      }
    })
    return unsub
  }, [])

  // クリーンアップ
  useEffect(() => {
    return () => {
      flashTimers.current.forEach(t => clearTimeout(t))
    }
  }, [])

  return { savedSnapshots, savedSnapshotsByPort, justSavedNodes, setSavedSnapshots, setSavedSnapshotsByPort }
}

// ============================================================
// Snapshot ピン状態フック
// ============================================================
export function usePinnedSnapshots() {
  const [pinnedNodes, setPinnedNodes] = useState<Set<string>>(new Set())

  const togglePin = (nodeId: string) => {
    const newPinned = !pinnedNodes.has(nodeId)
    wsClient.setSnapshotPin(nodeId, newPinned)
  }

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'snapshot_pin_changed') {
        setPinnedNodes(prev => {
          const next = new Set(prev)
          if (msg.pinned) next.add(msg.nodeInstanceId)
          else next.delete(msg.nodeInstanceId)
          return next
        })
      }
    })
    return unsub
  }, [])

  return { pinnedNodes, togglePin }
}

// ============================================================
// 永続ノード状態フック
// ============================================================
export function usePersistentNodes() {
  const [persistentNodes, setPersistentNodes] = useState<Map<string, PersistentNodeInfo>>(new Map())

  useEffect(() => {
    return wsClient.onMessage(msg => {
      if (msg.type === 'persistent_node_changed') {
        setPersistentNodes(new Map(msg.activeNodes.map(n => [n.nodeInstanceId, n])))
      }
    })
  }, [])

  return persistentNodes
}

// ============================================================
// Snapshot 履歴フック
// ============================================================
export interface SnapshotHistoryState {
  nodeInstanceId: string
  portName: string
  entries: SnapshotHistoryEntry[]
}

export function useSnapshotHistory(
  savedSnapshots: Map<string, SnapshotBadgeInfo>,
  setSavedSnapshots: Dispatch<SetStateAction<Map<string, SnapshotBadgeInfo>>>,
  setSavedSnapshotsByPort: Dispatch<SetStateAction<Map<string, SnapshotBadgeInfo>>>
) {
  const [historyState, setHistoryState] = useState<SnapshotHistoryState | null>(null)

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'snapshot_history') {
        setHistoryState({ nodeInstanceId: msg.nodeInstanceId, portName: msg.portName, entries: msg.entries })
      } else if (msg.type === 'snapshot_restored') {
        // 復元成功: savedSnapshots を更新（snapshot_saved と同等の処理）
        const now = new Date()
        const time = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}`
        const badge = { portName: msg.portName, valueType: msg.valueType, time, valueString: msg.valueString ?? null }
        setSavedSnapshots(prev => {
          const next = new Map(prev)
          next.set(msg.nodeInstanceId, badge)
          return next
        })
        setSavedSnapshotsByPort(prev => {
          const next = new Map(prev)
          next.set(snapshotPortKey(msg.nodeInstanceId, msg.portName), badge)
          return next
        })
        // 履歴を再取得して最新状態に更新
        setHistoryState(prev => prev ? { ...prev } : null)
        wsClient.getSnapshotHistory(msg.nodeInstanceId, msg.portName)
      } else if (msg.type === 'snapshot_cleared') {
        setSavedSnapshots(prev => {
          const next = new Map(prev)
          next.delete(msg.nodeInstanceId)
          return next
        })
        setSavedSnapshotsByPort(prev => {
          const next = new Map(prev)
          if (msg.portName) {
            next.delete(snapshotPortKey(msg.nodeInstanceId, msg.portName))
          } else {
            // portName 省略 = そのノードの全ポートをクリア（ClearSnapshotHandler.cs の仕様）
            const prefix = `${msg.nodeInstanceId}:`
            for (const key of next.keys()) {
              if (key.startsWith(prefix)) next.delete(key)
            }
          }
          return next
        })
        setHistoryState(null)
      }
    })
    return unsub
  }, [setSavedSnapshots])

  const requestHistory = useCallback((nodeInstanceId: string, portName?: string) => {
    wsClient.getSnapshotHistory(nodeInstanceId, portName)
  }, [])

  const restoreSnapshot = useCallback((nodeInstanceId: string, portName: string, historyIndex: number) => {
    wsClient.restoreSnapshot(nodeInstanceId, portName, historyIndex)
  }, [])

  const clearSnapshot = useCallback((nodeInstanceId: string, portName?: string) => {
    wsClient.clearSnapshot(nodeInstanceId, portName)
  }, [])

  const closeHistory = useCallback(() => setHistoryState(null), [])

  return { historyState, requestHistory, restoreSnapshot, clearSnapshot, closeHistory }
}
