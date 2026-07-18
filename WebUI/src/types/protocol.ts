// ============================================================
// WebSocket メッセージ型定義
// サーバー側 ServerDtos.cs と対応
// ============================================================

export interface NodePortInfo {
  name: string
  direction: 'input' | 'output'
  dataType: string
  isRequired: boolean
  description?: string
  /** 入力・出力ポート共通のインラインエディタ表示制御（opt-in）。true のときのみ表示、それ以外は非表示 */
  showInlineEditor?: boolean | null
}

export interface NodeTypeInfo {
  id: string
  category: string
  displayName: string
  description?: string
  /** ノード型のバージョン（SemVer想定）。未指定時は "1.0.0" 扱い。 */
  nodeVersion?: string
  ports: NodePortInfo[]
  filePath?: string
  /** NodeWebUiAttribute の JSON（WebUI プラグイン仕様）。未設定は標準表示。 */
  customWebUi?: string
  /** カスタムノード(.cs)の最終更新日時（ISO8601）。DLL経由のノードはundefined。 */
  lastModified?: string
}

export interface NodeListResponse {
  type: 'node_list_response'
  nodes: NodeTypeInfo[]
}

export interface ExecutionResultResponse {
  type: 'execution_result'
  success: boolean
  errorMessage?: string
  durationMs: number
  /** 断片実行時のみ設定。どの断片の結果かを識別する。 */
  fragmentId?: string
}

export interface ExecutionLogPush {
  type: 'execution_log'
  nodeInstanceId?: string
  message: string
  level: 'debug' | 'info' | 'warning' | 'error'
  timestampMs: number
}

export interface SaveGraphResponse {
  type: 'save_graph_response'
  success: boolean
  id?: string
}

export interface GraphSummary {
  id: string
  name: string
  description?: string
}

export interface ListGraphsResponse {
  type: 'list_graphs_response'
  graphs: GraphSummary[]
}

export interface LoadGraphResponse {
  type: 'load_graph_response'
  success: boolean
  graph?: NodeGraphData
}

export interface DeleteGraphResponse {
  type: 'delete_graph_response'
  success: boolean
}


export interface ErrorResponse {
  type: 'error'
  message: string
}

export interface ExecutionProgressPush {
  type: 'execution_progress'
  nodeInstanceId: string
  /** "running" | "done" | "error" */
  status: 'running' | 'done' | 'error'
  durationMs?: number
}

export interface SnapshotSavedMessage {
  type: 'snapshot_saved'
  nodeInstanceId: string
  portName: string
  valueType: string
  valueString?: string | null
}

export interface SnapshotPinChangedMessage {
  type: 'snapshot_pin_changed'
  nodeInstanceId: string
  pinned: boolean
}

export interface SnapshotHistoryEntry {
  valueType: string
  valueString?: string | null
  timestampMs: number
}

export interface SnapshotHistoryMessage {
  type: 'snapshot_history'
  nodeInstanceId: string
  portName: string
  entries: SnapshotHistoryEntry[]
}

export interface SnapshotRestoredMessage {
  type: 'snapshot_restored'
  nodeInstanceId: string
  portName: string
  valueType: string
  valueString?: string | null
}

export interface SnapshotClearedMessage {
  type: 'snapshot_cleared'
  nodeInstanceId: string
  portName: string
}

// SnapshotStore全クリア通知
export interface AllSnapshotsClearedMessage {
  type: 'all_snapshots_cleared'
}

// SnapshotStoreエントリ
export interface SnapshotStoreEntry {
  nodeInstanceId: string
  portName: string
  valueType: string
  valueString?: string | null
}

// SnapshotStore全状態レスポンス
export interface SnapshotStoreStateMessage {
  type: 'snapshot_store_state'
  entries: SnapshotStoreEntry[]
}

export interface NodeListUpdatedPush {
  type: 'node_list_updated'
  updatedNodeTypeIds?: string[]
}

export interface ScriptCompileErrorPush {
  type: 'script_compile_error'
  fileName: string
  errorMessage: string
}

export interface ExportNodesResponse {
  type: 'export_nodes_response'
  success: boolean
  savedPath?: string
  skippedTypeIds?: string[]
  errorMessage?: string
}

export interface PersistentNodeInfo {
  nodeInstanceId: string
  displayName: string
  graphName: string
}

export interface PersistentNodeChangedMessage {
  type: 'persistent_node_changed'
  activeNodes: PersistentNodeInfo[]
}

export interface WelcomeMessage {
  type: 'welcome'
  pluginVersion: string
  gameName: string
  runtimeType: string
}

export interface OpenGraphPush {
  type: 'open_graph_push'
  graphId: string
}

export type ServerMessage =
  | NodeListResponse
  | ExecutionResultResponse
  | ExecutionLogPush
  | ExecutionProgressPush
  | SaveGraphResponse
  | ListGraphsResponse
  | LoadGraphResponse
  | DeleteGraphResponse
  | SnapshotSavedMessage
  | SnapshotPinChangedMessage
  | SnapshotHistoryMessage
  | SnapshotRestoredMessage
  | SnapshotClearedMessage
  | AllSnapshotsClearedMessage
  | SnapshotStoreStateMessage
  | NodeListUpdatedPush
  | ScriptCompileErrorPush
  | ExportNodesResponse
  | PersistentNodeChangedMessage
  | WelcomeMessage
  | OpenGraphPush
  | ErrorResponse

// ============================================================
// グラフデータ型定義
// ============================================================

export interface NodePosition { x: number; y: number }

/** ユーザーが角ドラッグで手動リサイズしたサイズ。省略時は自動サイズ。 */
export interface NodeSize { width: number; height: number }

export interface NodeInstance {
  instanceId: string
  nodeTypeId: string
  /** ノード追加時点の nodeTypeVersion。旧グラフ互換のため省略可。 */
  nodeTypeVersion?: string
  position: NodePosition
  paramValues: Record<string, unknown>
  size?: NodeSize
}

export interface NodeConnection {
  fromNodeInstanceId: string
  fromPortName: string
  toNodeInstanceId: string
  toPortName: string
}

// ============================================================
// 断片グラフ連携型
// ============================================================

export interface FragmentDefinition {
  id: string
  name: string
  nodeInstanceIds: string[]
}

export interface FragmentLink {
  sourceSnapshotNodeInstanceId: string
  sourcePortName: string
  toNodeInstanceId: string
  toPortName: string
}

export interface NodeGroup {
  id: string
  name: string
  /** グループの用途・出力内容の説明（省略可能）。AI がグラフを読み書きする際の意図理解に使用。 */
  description?: string
  nodeInstanceIds: string[]
  collapsed: boolean
  color?: string
}

export interface NodeAnnotation {
  id: string
  text: string
  position: { x: number; y: number }
  width: number
  height: number
  color?: string
}

export interface NodeGraphData {
  id: string
  name: string
  description: string
  /**
   * グラフデータフォーマットのスキーマバージョン（セマンティックバージョン文字列）。
   * 存在しない / 空の場合は初期版（グループ機能なし）の旧グラフ。
   *   (なし/空) = 初期版
   *   "0.1.0"   = groups フィールド追加（2026-05-09）
   */
  schemaVersion?: string
  version: number
  createdAt: string
  nodes: NodeInstance[]
  connections: NodeConnection[]
  fragments: FragmentDefinition[]
  fragmentLinks: FragmentLink[]
  groups?: NodeGroup[]
  annotations?: NodeAnnotation[]
}

/** 現在サポートするスキーマバージョン（C# NodeGraph.CurrentSchemaVersion と同値に保つ）。 */
export const CURRENT_SCHEMA_VERSION = '0.2.0'
