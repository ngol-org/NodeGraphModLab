import type { ServerMessage, NodeGraphData } from '../types/protocol'
import { getAuthToken } from './authToken'

const WS_URL = import.meta.env.DEV
  ? 'ws://127.0.0.1:11156/ws'
  : `ws://${location.host}/ws`

type MessageHandler = (msg: ServerMessage) => void
type ConnectionHandler = (connected: boolean) => void

/** load_graph 要求の目的。load_graph_response の複数購読者が処理要否を判別するために使う（サーバーへは送信しない）。 */
export type LoadGraphPurpose = 'open' | 'import'

class GraphWebSocketClient {
  private ws: WebSocket | null = null
  private handlers: Set<MessageHandler> = new Set()
  private connectionHandlers: Set<ConnectionHandler> = new Set()
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private destroyed = false
  private loadPurpose: LoadGraphPurpose = 'open'

  connect() {
    if (this.ws?.readyState === WebSocket.OPEN) return
    if (this.ws?.readyState === WebSocket.CONNECTING) return

    const token = getAuthToken()
    this.ws = token ? new WebSocket(WS_URL, [token]) : new WebSocket(WS_URL)

    this.ws.onopen = () => {
      this.connectionHandlers.forEach(h => h(true))
      console.log('[WS] connected:', WS_URL)
    }

    this.ws.onmessage = (e) => {
      try {
        const msg = JSON.parse(e.data) as ServerMessage
        this.handlers.forEach(h => h(msg))
      } catch { /* 不正 JSON は無視 */ }
    }

    this.ws.onclose = () => {
      this.connectionHandlers.forEach(h => h(false))
      if (!this.destroyed) {
        this.reconnectTimer = setTimeout(() => this.connect(), 3000)
      }
    }

    this.ws.onerror = (e) => {
      console.warn('[WS] error', e)
    }
  }

  disconnect() {
    this.destroyed = true
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer)
    this.ws?.close()
    this.ws = null
  }

  /** 保留中の自動再接続タイマーを破棄し、即座に再接続する（トークン入力後の再試行用）。 */
  reconnectNow() {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.ws?.close()
    this.ws = null
    this.connect()
  }

  send(data: object) {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(data))
    } else {
      console.warn('[WS] not connected, message dropped')
    }
  }

  onMessage(handler: MessageHandler): () => void {
    this.handlers.add(handler)
    return () => { this.handlers.delete(handler) }
  }

  onConnection(handler: ConnectionHandler): () => void {
    this.connectionHandlers.add(handler)
    return () => { this.connectionHandlers.delete(handler) }
  }

  get isConnected() { return this.ws?.readyState === WebSocket.OPEN }

  // ---- API メソッド ----

  getNodeList() { this.send({ type: 'get_node_list' }) }

  executeGraph(graph: NodeGraphData) { this.send({ type: 'execute_graph', graph }) }

  saveGraph(graph: NodeGraphData) { this.send({ type: 'save_graph', graph }) }

  loadGraph(id: string, purpose: LoadGraphPurpose = 'open') {
    this.loadPurpose = purpose
    this.send({ type: 'load_graph', id })
  }

  /** 直近の loadGraph() 呼び出しの目的を返す。load_graph_response 受信時に購読者が参照する。 */
  getLoadPurpose(): LoadGraphPurpose { return this.loadPurpose }

  listGraphs() { this.send({ type: 'list_graphs' }) }

  deleteGraph(id: string) { this.send({ type: 'delete_graph', id }) }

  stopGraph() { this.send({ type: 'stop_graph' }) }

  /** 指定断片のみを実行する。上流 snapshot が空なら自動カスケード実行。 */
  executeFragment(graph: NodeGraphData, fragmentId: string, pinnedFragmentIds: string[] = []) {
    this.send({ type: 'execute_fragment', graph, fragmentId, pinnedFragmentIds })
  }

  /** 断片 DAG トポロジカル順に全断片を実行。pinnedFragmentIds の断片はスキップ。 */
  executeAllFragments(graph: NodeGraphData, pinnedFragmentIds: string[] = []) {
    this.send({ type: 'execute_all_fragments', graph, pinnedFragmentIds })
  }

  /** Snapshot ノードのピン状態をサーバーに通知する。 */
  setSnapshotPin(nodeInstanceId: string, pinned: boolean) {
    this.send({ type: 'set_snapshot_pin', nodeInstanceId, pinned })
  }

  /** 指定ノードの Snapshot 履歴を取得する。portName は省略可（サーバーが自動解決）。 */
  getSnapshotHistory(nodeInstanceId: string, portName?: string) {
    this.send({ type: 'get_snapshot_history', nodeInstanceId, ...(portName ? { portName } : {}) })
  }

  /** 指定インデックスの過去値を現在値として復元する。 */
  restoreSnapshot(nodeInstanceId: string, portName: string, historyIndex: number) {
    this.send({ type: 'restore_snapshot', nodeInstanceId, portName, historyIndex })
  }

  /** 指定ポートの Snapshot（現在値＋履歴）をクリアする。portName を省略するとノード全体をクリア。 */
  clearSnapshot(nodeInstanceId: string, portName?: string) {
    this.send({ type: 'clear_snapshot', nodeInstanceId, ...(portName ? { portName } : {}) })
  }

  /** SnapshotStore の全エントリをクリアする */
  clearAllSnapshots() { this.send({ type: 'clear_all_snapshots' }) }

  /** SnapshotStore の全状態を取得する */
  getSnapshotStoreState() { this.send({ type: 'get_snapshot_store_state' }) }

  /** 選択ノードを 1 つの DLL パックにコンパイルして保存する。 */
  exportNodesAsDll(assemblyName: string, outputDir: string, nodeTypeIds: string[]) {
    this.send({ type: 'export_nodes', assemblyName, outputDir, nodeTypeIds })
  }

  /** 全永続ノードを停止する。 */
  stopPersistent() { this.send({ type: 'stop_persistent' }) }

  /** 指定ノードの永続コールバックを停止する。 */
  stopPersistentNode(nodeInstanceId: string) { this.send({ type: 'stop_persistent_node', nodeInstanceId }) }

  /** ノードタイプのファイルをエクスプローラーで開く（スクリプトノード専用）。 */
  openNodeFolder(nodeTypeId: string) { this.send({ type: 'open_node_folder', nodeTypeId }) }
}

export const wsClient = new GraphWebSocketClient()
