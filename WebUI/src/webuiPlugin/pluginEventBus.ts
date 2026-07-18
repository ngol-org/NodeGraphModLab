import { wsClient } from '../lib/wsClient'
import type { ServerMessage } from '../types/protocol'

// ────────────────────────────────────────────────────────────────
// プラグイン向けイベントバス
// WebSocket 受信メッセージと UI 起因イベントを NGOL.events.on で購読可能にする。
// リスナーの例外は捕捉してログのみ（バスを止めない）。
// ────────────────────────────────────────────────────────────────

/** プラグインが購読できるイベント種別（初期セット）。 */
export type NgolEventType =
  | 'ws_message'          // 全 WebSocket 受信メッセージ
  | 'graph_loaded'        // グラフ読み込み完了（load_graph_response success）
  | 'execution_finished'  // グラフ / 断片実行完了（execution_result）
  | 'snapshot_saved'      // スナップショット保存
  | 'selection_changed'   // ノード選択変更 { selectedNodeIds: string[] }

type Listener = (payload: unknown) => void

const listeners = new Map<NgolEventType, Set<Listener>>()

/** イベントを購読する。戻り値は購読解除関数。 */
export function onPluginEvent(type: NgolEventType, cb: Listener): () => void {
  let set = listeners.get(type)
  if (!set) {
    set = new Set()
    listeners.set(type, set)
  }
  set.add(cb)
  return () => { set!.delete(cb) }
}

/** イベントを発火する（本体 UI 側から呼ぶ）。リスナー例外は個別に捕捉。 */
export function emitPluginEvent(type: NgolEventType, payload: unknown): void {
  const set = listeners.get(type)
  if (!set || set.size === 0) return
  set.forEach(cb => {
    try {
      cb(payload)
    } catch (e) {
      console.warn(`[NGOL Plugin] event listener error (${type}):`, e)
    }
  })
}

let initialized = false

/**
 * WebSocket 受信メッセージをイベントへマップする購読を開始する。
 * installNgolGlobalApi() から一度だけ呼ばれる（多重呼び出しは無視）。
 */
export function initPluginEventBus(): void {
  if (initialized) return
  initialized = true

  wsClient.onMessage((msg: ServerMessage) => {
    emitPluginEvent('ws_message', msg)
    switch (msg.type) {
      case 'load_graph_response':
        if ((msg as { success?: boolean }).success) emitPluginEvent('graph_loaded', msg)
        break
      case 'execution_result':
        emitPluginEvent('execution_finished', msg)
        break
      case 'snapshot_saved':
        emitPluginEvent('snapshot_saved', msg)
        break
    }
  })
}
