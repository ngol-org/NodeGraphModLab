import { useEffect, useRef } from 'react'
import { wsClient } from '../lib/wsClient'
import type { NodeGraphData } from '../types/protocol'

interface UseCanvasGraphRequestParams {
  /** 現在のキャンバスを保存形式に組み立てる。保存時と同じものを使う。 */
  buildGraphData: () => NodeGraphData
}

/**
 * 「今このタブが表示しているキャンバスを送れ」という問い合わせへ応じる。
 *
 * 保存を挟まずに現在の内容を渡せるようにするための経路で、
 * キャンバスを変更することは無い（読み取り専用）。
 */
export function useCanvasGraphRequest({ buildGraphData }: UseCanvasGraphRequestParams) {
  const buildGraphDataRef = useRef(buildGraphData)
  buildGraphDataRef.current = buildGraphData

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type !== 'canvas_graph_request_push') return
      wsClient.sendCanvasGraphResult(msg.requestToken, buildGraphDataRef.current())
    })
    return unsub
  }, [])
}
