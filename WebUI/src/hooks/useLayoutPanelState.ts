import { useState, useCallback, useEffect } from 'react'
import { getPluginPanels } from '../webuiPlugin/pluginPanelRegistry'
import {
  setDebugBridgeEnabled as applyDebugBridge,
  setDebugDetailLevel as applyDebugDetailLevel,
  setDebugNodeField as applyDebugNodeField,
  type DebugDetailLevel,
  type DebugNodeField,
} from '../webuiPlugin/debugBridge'

// ActiveLog（ExecutionLogPanel）の可変高さ設定
export const LOG_PANEL_MIN_HEIGHT = 80
export const LOG_PANEL_DEFAULT_HEIGHT = 180

export function useLayoutPanelState() {
  // パネル表示状態（パネル単位で保持。デフォルトは左右カラム折りたたみ・ミニマップ表示）
  // UI操作は現状カラム単位のみだが、将来パネル個別トグルを追加する際に state モデル・幅計算ロジックを
  // 書き直さずに済むよう、あえてパネル単位の4つの state として持つ。
  const [nodePaletteVisible, setNodePaletteVisible] = useState(false)
  const [graphListVisible, setGraphListVisible] = useState(false)
  const [nodeInspectorVisible, setNodeInspectorVisible] = useState(false)
  const [fragmentPanelVisible, setFragmentPanelVisible] = useState(false)
  const [minimapVisible, setMinimapVisible] = useState(true)
  // File > Open Graph サブメニューが開いている間、GraphListPanel 側の load_graph_response 二重処理を防ぐ
  const [fileMenuGraphSubmenuOpen, setFileMenuGraphSubmenuOpen] = useState(false)
  const [logPanelVisible, setLogPanelVisible] = useState(true)
  const [logPanelHeight, setLogPanelHeightState] = useState(LOG_PANEL_DEFAULT_HEIGHT)
  const setLogPanelHeight = useCallback((height: number, maxHeight: number) => {
    setLogPanelHeightState(Math.min(Math.max(height, LOG_PANEL_MIN_HEIGHT), maxHeight))
  }, [])
  const [debugBridgeEnabled, setDebugBridgeEnabled] = useState(false)
  // B-1/B-2: dom_event の reactFlowNodes ダンプ詳細度（既定 minimal = 含めない）
  const [debugDetailLevel, setDebugDetailLevel] = useState<DebugDetailLevel>('minimal')
  // B-3: reactFlowNodes の各要素に含めるフィールド（既定は全て含む）
  const [debugNodeFields, setDebugNodeFieldsState] = useState<Record<DebugNodeField, boolean>>({
    rect: true,
    pointerEvents: true,
    visibility: true,
    transform: true,
  })
  const setDebugNodeField = useCallback((field: DebugNodeField, value: boolean) => {
    setDebugNodeFieldsState(prev => ({ ...prev, [field]: value }))
  }, [])
  // ReactFlow Controls の interactivity ロックボタン状態（見た目強調用。ボタン自体は ReactFlow 標準実装）
  const [controlsLocked, setControlsLocked] = useState(false)

  const leftColumnCollapsed = !nodePaletteVisible && !graphListVisible
  const rightColumnCollapsed = !nodeInspectorVisible && !fragmentPanelVisible

  const toggleLeftColumn = useCallback(() => {
    setNodePaletteVisible(v => !v)
    setGraphListVisible(v => !v)
  }, [])

  const toggleRightColumn = useCallback(() => {
    setNodeInspectorVisible(v => !v)
    setFragmentPanelVisible(v => !v)
  }, [])

  // 外部プラグインパネルの開閉状態（初期値: defaultOpen 指定パネルのみ開く）
  const [openPluginPanelIds, setOpenPluginPanelIds] = useState<string[]>(
    () => getPluginPanels().filter(p => p.defaultOpen).map(p => p.id)
  )

  const togglePluginPanel = useCallback((panelId: string) => {
    setOpenPluginPanelIds(prev =>
      prev.includes(panelId) ? prev.filter(id => id !== panelId) : [...prev, panelId]
    )
  }, [])

  useEffect(() => {
    applyDebugBridge(debugBridgeEnabled)
  }, [debugBridgeEnabled])

  useEffect(() => {
    applyDebugDetailLevel(debugDetailLevel)
  }, [debugDetailLevel])

  useEffect(() => {
    for (const [field, value] of Object.entries(debugNodeFields)) {
      applyDebugNodeField(field as DebugNodeField, value)
    }
  }, [debugNodeFields])

  return {
    nodePaletteVisible,
    graphListVisible,
    nodeInspectorVisible,
    fragmentPanelVisible,
    minimapVisible,
    setMinimapVisible,
    fileMenuGraphSubmenuOpen,
    setFileMenuGraphSubmenuOpen,
    logPanelVisible,
    setLogPanelVisible,
    logPanelHeight,
    setLogPanelHeight,
    debugBridgeEnabled,
    setDebugBridgeEnabled,
    debugDetailLevel,
    setDebugDetailLevel,
    debugNodeFields,
    setDebugNodeField,
    controlsLocked,
    setControlsLocked,
    leftColumnCollapsed,
    rightColumnCollapsed,
    toggleLeftColumn,
    toggleRightColumn,
    openPluginPanelIds,
    togglePluginPanel,
  }
}
