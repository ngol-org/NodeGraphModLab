import { useCallback, useEffect, useRef } from 'react'
import type { ReactFlowInstance } from '@xyflow/react'
import { wsClient } from '../lib/wsClient'
import { writeHandoff, consumeHandoff } from '../lib/reloadHandoff'
import type { NodeGraphData } from '../types/protocol'
import type { useExecutionLogs } from './useGraphEditor'

interface UseWebUiReloadParams {
  /** 現在のキャンバスを保存形式に組み立てる。 */
  buildGraphData: () => NodeGraphData
  /** 組み立てたグラフをキャンバスへ適用する。 */
  applyGraph: (graph: NodeGraphData) => void
  rfRef: React.RefObject<ReactFlowInstance | null>
  /** ノード型一覧が届いているか。届く前に適用すると型情報なしのノードになる。 */
  nodeTypesReady: boolean
  addLog: ReturnType<typeof useExecutionLogs>['addLog']
}

/**
 * NGOL 発のページリロードと、その前後でのキャンバス状態の引き継ぎ。
 *
 * 入口はメニュー・ツールバー・MCP プッシュの3つあるが、すべて reloadNow() に合流する。
 * 素の F5（リロードボタン含む）は引き継ぎを行わず、従来どおり新規キャンバスで起動する。
 */
export function useWebUiReload({
  buildGraphData,
  applyGraph,
  rfRef,
  nodeTypesReady,
  addLog,
}: UseWebUiReloadParams) {
  const buildGraphDataRef = useRef(buildGraphData)
  buildGraphDataRef.current = buildGraphData
  const applyGraphRef = useRef(applyGraph)
  applyGraphRef.current = applyGraph
  const addLogRef = useRef(addLog)
  addLogRef.current = addLog

  const restoredRef = useRef(false)

  const reloadNow = useCallback(() => {
    const viewport = rfRef.current?.getViewport()
    const ok = writeHandoff({ graph: buildGraphDataRef.current(), ...(viewport ? { viewport } : {}) })
    if (!ok) {
      // 書けないままリロードすると状態が失われる。中止して保存を促す。
      addLogRef.current({
        timestampMs: Date.now(),
        level: 'error',
        category: 'notify',
        message: 'Reload aborted: could not preserve the canvas state (storage unavailable or full). Save the graph first.',
      })
      return
    }
    location.reload()
  }, [rfRef])

  // ノード型一覧が届いてから1度だけ復元する。届く前は nodeTypeMap が空で、
  // 適用すると全ノードが型情報なしになる。
  useEffect(() => {
    if (!nodeTypesReady || restoredRef.current) return
    restoredRef.current = true

    const handoff = consumeHandoff()
    if (!handoff) return

    applyGraphRef.current(handoff.graph)
    const viewport = handoff.viewport
    if (viewport) {
      // 初期表示の fitView より後に当てる必要がある。
      requestAnimationFrame(() => rfRef.current?.setViewport(viewport))
    }
    addLogRef.current({
      timestampMs: Date.now(),
      level: 'info',
      category: 'notify',
      message: `Restored canvas after reload: ${handoff.graph.name} (unsaved changes kept)`,
    })
  }, [nodeTypesReady, rfRef])

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type !== 'reload_webui_push') return
      if (msg.preserveState === false) {
        location.reload()
        return
      }
      reloadNow()
    })
    return unsub
  }, [reloadNow])

  return { reloadNow }
}
