import { memo, useState, useSyncExternalStore } from 'react'
import { Handle, Position, type NodeProps, useReactFlow } from '@xyflow/react'
import type { NodeTypeInfo, NodeSize } from '../types/protocol'
import { NodeResizeHandles } from './NodeResizeHandle'
import { rgbaCssColor, toRgbaColor } from './colorUtils'
import { wsClient } from '../lib/wsClient'
import './CustomNode.css'
import './SnapshotNode.css'
import { useExecuteFragment } from '../contexts/ExecuteFragmentContext'
import { resolveFromCustomWebUiJson } from '../webuiPlugin/webuiPluginRegistry'
import type { NodeRendererProps, WidgetProps } from '../webuiPlugin/webuiPluginRegistry'
import { PluginErrorBoundary } from '../webuiPlugin/PluginErrorBoundary'
import { NodeOverrideErrorBoundary } from '../webuiPlugin/NodeOverrideErrorBoundary'
import {
  resolveNodeTypeOverride,
  subscribeNodeTypeOverrides,
  getNodeTypeOverrideSnapshot,
} from '../webuiPlugin/nodeTypeOverrideRegistry'
import { NgolIcon } from './icons/NgolIcon'
import { getPluginCategoryColor } from '../webuiPlugin/categoryColorRegistry'
import { EXEC_IN_PORT_NAME, EXEC_OUT_PORT_NAME } from '../lib/dragAddNode'
import { autoPortLayout, EXEC_TOP, PORT_BASE } from '../lib/nodePortLayout'
import { shouldShowPortInlineEditor } from '../lib/portInlineEditor'
import { isNodeVersionMismatch, resolveCurrentNodeTypeVersion } from '../lib/nodeVersion'
import { NodeVersionMismatchBadge } from './NodeVersionMismatchBadge'

// ────────────────────────────────────────────────────────────────
// 型定義
// ────────────────────────────────────────────────────────────────
export interface SnapshotBadgeInfo {
  portName: string
  valueType: string
  time: string
  valueString?: string | null
}

export interface CustomNodeData extends Record<string, unknown> {
  label: string
  nodeTypeId: string
  nodeTypeInfo?: NodeTypeInfo
  paramValues: Record<string, unknown>
  snapshotBadge?: SnapshotBadgeInfo | null
  /** このノードの全ポート分のバッジ（ポート名 → バッジ）。プラグインへそのまま渡す。 */
  snapshotBadgesByPort?: Record<string, SnapshotBadgeInfo>
  snapshotPinned?: boolean
  snapshotJustSaved?: boolean
  snapshotShowToString?: boolean
  stale?: boolean
  /** ノードタイプがレジストリから削除された（Nodes/ファイル削除後） */
  removed?: boolean
  /** このノードが属する断片ID（T13） */
  fragmentId?: string | null
  /** 複数ノードが選択されている状態か（断片Runボタン非表示制御） */
  isMultiSelect?: boolean
  /** 永続コールバックが実行中か */
  isPersistent?: boolean
  /** ユーザーが角ドラッグで手動リサイズしたサイズ。未設定 = 自動サイズ */
  size?: NodeSize
  /** グラフ保存時点の nodeTypeVersion */
  nodeTypeVersion?: string
}

// ────────────────────────────────────────────────────────────────
// カテゴリ別アクセントカラー
// ────────────────────────────────────────────────────────────────
const CATEGORY_COLORS: Record<string, string> = {
  Logic: '#e94560',
  Math: '#2196f3',
  String: '#4caf50',
  Control: '#ff9800',
  Debug: '#9e9e9e',
}
function getCategoryColor(category: string): string {
  return getPluginCategoryColor(category) ?? CATEGORY_COLORS[category] ?? '#3a3a5c'
}

/** 最下段 Handle 中心 + 半径 + 下余白 */
const PLUGIN_MIN_HEIGHT_PAD = 14

function pluginMinHeightFromLayout(layout: { inputs: Record<string, number>; outputs: Record<string, number> }): number {
  const tops = [
    EXEC_TOP,
    ...Object.values(layout.inputs),
    ...Object.values(layout.outputs),
  ]
  return Math.max(...tops, PORT_BASE) + PLUGIN_MIN_HEIGHT_PAD
}

// ────────────────────────────────────────────────────────────────
// CustomNode コンポーネント
// ────────────────────────────────────────────────────────────────
export const CustomNode = memo(function CustomNode({ id, data, selected }: NodeProps) {
  const nodeData = data as CustomNodeData
  const typeInfo = nodeData.nodeTypeInfo
  const savedNodeTypeVersion = nodeData.nodeTypeVersion
  const currentNodeTypeVersion = resolveCurrentNodeTypeVersion(typeInfo?.nodeVersion)
  const showVersionMismatchBadge = isNodeVersionMismatch(savedNodeTypeVersion, typeInfo?.nodeVersion)
  const inputPorts  = typeInfo?.ports.filter(p => p.direction === 'input')  ?? []
  const outputPorts = typeInfo?.ports.filter(p => p.direction === 'output') ?? []
  const category    = typeInfo?.category ?? ''
  const accentColor = getCategoryColor(category)
  const { updateNodeData, getEdges, getNode } = useReactFlow()
  const executeFragmentCtx = useExecuteFragment()

  // ノード型 ID 上書き — Plugins メニューのトグル変更に追従するためストア購読
  const overrideSnapshot = useSyncExternalStore(subscribeNodeTypeOverrides, getNodeTypeOverrideSnapshot)
  // G3: このノードインスタンスで上書き描画が例外を投げたら標準描画へフォールバック
  const [overrideFailed, setOverrideFailed] = useState(false)

  // paramValues 変更ハンドラ
  // 関数型更新必須: 同一イベント内で複数回呼ばれた場合（例: XY パッドの x/y 同時更新）、
  // クロージャの古い paramValues を展開すると後の呼び出しが前の更新を上書きしてしまう
  const handleParamChange = (portName: string, value: unknown) => {
    // NodeRenderer/Widget プラグインは body を自由描画できるため、ポート名のハードコードにより
    // ライブの NodeRegistry から消えた/リネームされたポートを描画し続けるバグを作り込みうる。
    // ここは全プラグインの onParamChange が必ず通る唯一の入口なので、ここで検知を強制する。
    if (typeInfo && !inputPorts.some(p => p.name === portName)) {
      console.error(
        `[NGOL] Node '${typeInfo.id}' (instance ${id}): onParamChange called for unknown port '${portName}'. ` +
        `This node's WebUI plugin likely hardcodes a stale port name — check its [NodeWebUi] component ` +
        `against the current nodeTypeInfo.ports instead of a literal list.`
      )
    }
    updateNodeData(id, (node) => ({
      paramValues: { ...(node.data as CustomNodeData).paramValues, [portName]: value },
    }))
  }

  // 角ドラッグリサイズ。標準ノード（非フルノード描画プラグイン）のみ対応。
  const handleResize = (width: number, height: number) => {
    updateNodeData(id, () => ({ size: { width, height } }))
  }

  // 入力欄フォーカス中に Ctrl+Enter で断片実行
  const handleInputKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && e.ctrlKey && executeFragmentCtx && nodeData.fragmentId) {
      e.preventDefault()
      e.stopPropagation()
      if (!selected || nodeData.isMultiSelect) {
        executeFragmentCtx.selectNode(id)
      }
      executeFragmentCtx.executeFragmentForNode(id)
    }
  }

  // ノード div またはノード内要素（input 等）のフォーカス時にノード選択を切り替える
  const handleNodeFocus = (_e: React.FocusEvent<HTMLDivElement>) => {
    executeFragmentCtx?.selectNode(id)
  }

  // ノード div にフォーカス中の Ctrl+Enter で断片実行
  const handleNodeKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && e.ctrlKey && executeFragmentCtx && nodeData.fragmentId) {
      e.preventDefault()
      executeFragmentCtx.executeFragmentForNode(id)
    }
  }

  // ノードタイプが削除された場合のデグレード表示（ポートなし、ID のみ）
  if (nodeData.removed) {
    return (
      <div
        className={`custom-node${selected ? ' selected' : ''}`}
        style={{ '--node-accent': '#555' } as React.CSSProperties}
      >
        <div className="custom-node-header" style={{ opacity: 0.6 }}>
          <span className="custom-node-title" style={{ fontFamily: 'monospace', fontSize: 10 }}>
            {nodeData.nodeTypeId}
          </span>
          <span
            title="This node type has been removed"
            className="ngol-btn-with-icon"
            style={{ marginLeft: 4, background: '#555', borderRadius: 3, padding: '0 4px', fontSize: 9, color: '#ccc', verticalAlign: 'middle' }}
          ><NgolIcon name="close" size={9} className="ngol-icon" /> Removed</span>
        </div>
      </div>
    )
  }

  const isSnapshot = nodeData.nodeTypeId === 'ngol.snapshot' || nodeData.nodeTypeId?.startsWith('ngol.snapshot.')
  const isUnifiedSnapshot = nodeData.nodeTypeId === 'ngol.snapshot'
  const isAnySnapshot = nodeData.nodeTypeId === 'ngol.snapshot.any'

  // 統合Snapshot / AnySnapshot: 上流ポートのDataTypeをエッジから取得
  let upstreamDataType: string | null = null
  if (isUnifiedSnapshot || isAnySnapshot) {
    const inEdge = getEdges().find(e => e.target === id && e.targetHandle === 'value')
    if (inEdge) {
      const srcNode = getNode(inEdge.source)
      const srcTypeInfo = (srcNode?.data as CustomNodeData | undefined)?.nodeTypeInfo
      const srcPort = srcTypeInfo?.ports.find(p => p.name === inEdge.sourceHandle && p.direction === 'output')
      upstreamDataType = srcPort?.dataType ?? null
    }
  }

  // 型専用Snapshot: 上流型が接続済みかつ期待型と不一致なら警告
  const isTypedSnapshot = isSnapshot && !isAnySnapshot && !isUnifiedSnapshot
  let typeMismatchWarning = false
  if (isTypedSnapshot && inputPorts.length > 0) {
    const expectedType = inputPorts[0].dataType.toLowerCase()
    const portName = inputPorts[0].name
    const inEdge = getEdges().find(e => e.target === id && e.targetHandle === portName)
    if (inEdge) {
      const srcNode = getNode(inEdge.source)
      const srcTypeInfo = (srcNode?.data as CustomNodeData | undefined)?.nodeTypeInfo
      const srcPort = srcTypeInfo?.ports.find(p => p.name === inEdge.sourceHandle && p.direction === 'output')
      if (srcPort && srcPort.dataType.toLowerCase() !== expectedType) {
        typeMismatchWarning = true
      }
    }
  }

  const portCount = Math.max(inputPorts.length, outputPorts.length, 1)
  // +14: 実ポートの有無に関わらず常時描画される合成 exec ポート行の高さ分
  const bodyHeight = portCount * 24 + 8 + 14

  // ――― UI プラグイン解決 ―――――――――――――――――――――――――――――――
  // 優先順位: 1. ノード型ID上書き → 2. [NodeWebUi] 宣言 → 3. 標準描画
  // 型ID上書きが存在する場合、宣言側プラグインは kind を問わず使用しない（上書きが全面的に勝つ）。
  const uiPlugin = resolveFromCustomWebUiJson(nodeData.nodeTypeInfo?.customWebUi)
  const typeOverride = !overrideFailed
    ? resolveNodeTypeOverride(overrideSnapshot, nodeData.nodeTypeId)
    : null

  // 型ID上書きと宣言プラグインを共通形式に正規化。
  // overrideLabel が non-null のものが型ID上書き（G1 バッジ表示・G3 フォールバック対象）。
  const activeUi = typeOverride
    ? typeOverride.kind === 'node'
      ? { kind: 'node' as const, Component: typeOverride.component as React.FC<NodeRendererProps>, options: typeOverride.options, spec: {} as Record<string, unknown>, overrideLabel: typeOverride.label }
      : { kind: 'widget' as const, Component: typeOverride.component as React.FC<WidgetProps>, spec: {} as Record<string, unknown>, overrideLabel: typeOverride.label }
    : uiPlugin
      ? uiPlugin.kind === 'node'
        ? { kind: 'node' as const, Component: uiPlugin.Component, options: uiPlugin.options, spec: uiPlugin.spec, overrideLabel: null }
        : { kind: 'widget' as const, Component: uiPlugin.Component, spec: uiPlugin.spec, overrideLabel: null }
      : null

  // ――― フルノード描画モード ―――――――――――――――――――――――――――――――
  // 外枠・ポート Handle・断片実行ボタンは NGOL が管理し、内側の描画全体をプラグインに委譲する。
  if (activeUi?.kind === 'node' && typeInfo) {
    const uiSourceId = activeUi.overrideLabel ?? String(activeUi.spec.pluginId ?? '')
    // Handle 縦位置: プラグインの portLayout 指定 > 既定レイアウト（top PORT_BASE(68px) から PORT_STEP(26px) 間隔）
    let layout = autoPortLayout(typeInfo)
    if (activeUi.options.portLayout) {
      try {
        layout = activeUi.options.portLayout(typeInfo)
      } catch (e) {
        console.warn(`[NGOL Plugin] portLayout error in '${uiSourceId}':`, e)
      }
    }
    const runFragment = () => {
      if (executeFragmentCtx && nodeData.fragmentId) {
        executeFragmentCtx.executeFragmentForNode(id)
      }
    }
    const pluginMinHeight = pluginMinHeightFromLayout(layout)
    return (
      <div
        className={`custom-node custom-node-plugin${selected ? ' selected' : ''}${nodeData.snapshotPinned ? ' snapshot-pinned' : ''}${nodeData.snapshotJustSaved ? ' snapshot-just-saved' : ''}${nodeData.isPersistent ? ' persistent-running' : ''}`}
        style={{ '--node-accent': accentColor, minHeight: pluginMinHeight } as React.CSSProperties}
        tabIndex={0}
        onFocus={handleNodeFocus}
        onKeyDown={handleNodeKeyDown}
      >
        {/* [NodeWebUi] 宣言によるフルノード描画である印（常時表示・右上隅）。
            通常ノードとの見分けがつかず、プラグイン側のポート表示が古いままでも気づきにくい問題への対策。 */}
        {activeUi.overrideLabel == null && (
          <span
            className="ngol-btn-with-icon"
            title={`Custom body rendered by WebUI plugin: ${uiSourceId} (declared via [NodeWebUi]). If ports/labels look outdated after a server-side change, check this plugin's source — it may not read live port data.`}
            style={{ position: 'absolute', top: 4, right: 4, zIndex: 5, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 18, height: 18, background: '#00897b', borderRadius: 3 }}
          ><NgolIcon name="puzzle" size={13} className="ngol-icon" /></span>
        )}

        {/* 選択時: 断片実行ボタン — 複数選択中は非表示。永続実行中は停止ボタンに切り替え */}
        {selected && !nodeData.isMultiSelect && (nodeData.isPersistent || (nodeData.fragmentId && executeFragmentCtx)) && (
          <button
            className={`node-execute-fragment-btn${nodeData.isPersistent ? ' stop-mode' : ''}`}
            title={nodeData.isPersistent ? 'Stop persistent execution' : 'Run fragment containing this node'}
            onMouseDown={e => e.stopPropagation()}
            onPointerDown={e => e.stopPropagation()}
            onMouseEnter={() => nodeData.fragmentId && executeFragmentCtx?.setHoveredFragmentId(nodeData.fragmentId)}
            onMouseLeave={() => executeFragmentCtx?.setHoveredFragmentId(null)}
            onClick={e => {
              e.stopPropagation()
              if (nodeData.isPersistent) {
                wsClient.stopPersistentNode(id)
              } else {
                executeFragmentCtx!.executeFragmentForNode(id)
              }
            }}
          >
            <NgolIcon name={nodeData.isPersistent ? 'stop' : 'play'} size={12} />
          </button>
        )}

        {/* 標準ノードと同じヘッダー（区切り線付き）。タイトルは NGOL 側で描画しプラグインは body のみ担当 */}
        <div className="custom-node-header">
          {category && (
            <span className="custom-node-category">{category}</span>
          )}
          <span className="custom-node-title">{nodeData.label}</span>
          {showVersionMismatchBadge && savedNodeTypeVersion && (
            <NodeVersionMismatchBadge
              savedVersion={savedNodeTypeVersion}
              currentVersion={currentNodeTypeVersion}
            />
          )}
          {activeUi.overrideLabel != null && (
            <span
              className="ngol-btn-with-icon"
              title={`WebUI plugin override active: ${activeUi.overrideLabel}`}
              style={{ marginLeft: 4, background: '#7c4dff', borderRadius: 3, padding: '0 4px', fontSize: 9, color: '#fff', verticalAlign: 'middle' }}
            ><NgolIcon name="edit" size={9} className="ngol-icon" /> Override</span>
          )}
        </div>

        {/* 合成 exec ポート — ヘッダー区切り線の下（標準ノード body 先頭行相当） */}
        <Handle
          type="target"
          position={Position.Left}
          id={EXEC_IN_PORT_NAME}
          className="custom-handle input-handle exec-handle"
          style={{ top: EXEC_TOP }}
          title="Execution order (no data) — connect a node here to run this node after it"
        />
        <Handle
          type="source"
          position={Position.Right}
          id={EXEC_OUT_PORT_NAME}
          className="custom-handle output-handle exec-handle"
          style={{ top: EXEC_TOP }}
          title="Execution order (no data) — connect this to run the next node after this one"
        />

        {/* データポート Handle（NGOL 管理・接続線アンカー） */}
        {inputPorts.map(port => (
          <Handle
            key={port.name}
            type="target"
            position={Position.Left}
            id={port.name}
            className="custom-handle input-handle"
            style={{ top: layout.inputs[port.name] ?? PORT_BASE }}
            title={`${port.name}: ${port.dataType}`}
          />
        ))}
        {outputPorts.map(port => (
          <Handle
            key={port.name}
            type="source"
            position={Position.Right}
            id={port.name}
            className="custom-handle output-handle"
            style={{ top: layout.outputs[port.name] ?? PORT_BASE }}
            title={`${port.name}: ${port.dataType}`}
          />
        ))}

        {/* 内側描画をプラグインへ委譲（ヘッダー直下の body 領域） */}
        <div className="custom-node-plugin-body">
        {activeUi.overrideLabel != null ? (
          <NodeOverrideErrorBoundary
            nodeTypeId={nodeData.nodeTypeId}
            onError={() => setOverrideFailed(true)}
          >
            <activeUi.Component
              spec={activeUi.spec}
              nodeId={id}
              nodeTypeInfo={typeInfo}
              paramValues={nodeData.paramValues}
              onParamChange={handleParamChange}
              snapshotBadge={nodeData.snapshotBadge ?? null}
              snapshotBadgesByPort={nodeData.snapshotBadgesByPort}
              selected={selected ?? false}
              runFragment={runFragment}
            />
          </NodeOverrideErrorBoundary>
        ) : (
          <PluginErrorBoundary pluginId={uiSourceId}>
            <activeUi.Component
              spec={activeUi.spec}
              nodeId={id}
              nodeTypeInfo={typeInfo}
              paramValues={nodeData.paramValues}
              onParamChange={handleParamChange}
              snapshotBadge={nodeData.snapshotBadge ?? null}
              snapshotBadgesByPort={nodeData.snapshotBadgesByPort}
              selected={selected ?? false}
              runFragment={runFragment}
            />
          </PluginErrorBoundary>
        )}
        </div>

        {/* 永続実行中バッジ */}
        {nodeData.isPersistent && (
          <div className="persistent-node-badge">⚙ PERSISTENT</div>
        )}
      </div>
    )
  }

  const manualSize = nodeData.size
  return (
    <div
      className={`custom-node${selected ? ' selected' : ''}${nodeData.snapshotPinned ? ' snapshot-pinned' : ''}${nodeData.snapshotJustSaved ? ' snapshot-just-saved' : ''}${nodeData.isPersistent ? ' persistent-running' : ''}${manualSize ? ' node-manually-resized' : ''}`}
      style={{
        '--node-accent': accentColor,
        ...(manualSize ? { width: manualSize.width, height: manualSize.height } : {}),
      } as React.CSSProperties}
      tabIndex={0}
      onFocus={handleNodeFocus}
      onKeyDown={handleNodeKeyDown}
    >
      {/* 選択時: 角ドラッグでリサイズ */}
      {selected && (
        <NodeResizeHandles
          nodeId={id}
          targetSelector=".custom-node"
          minWidth={160}
          minHeight={bodyHeight + 60}
          onResize={handleResize}
        />
      )}
      {/* 選択時: 断片実行ボタン (T13) — 複数選択中は非表示。永続実行中は停止ボタンに切り替え */}
      {selected && !nodeData.isMultiSelect && (nodeData.isPersistent || (nodeData.fragmentId && executeFragmentCtx)) && (
        <button
          className={`node-execute-fragment-btn${nodeData.isPersistent ? ' stop-mode' : ''}`}
          title={nodeData.isPersistent ? 'Stop persistent execution' : 'Run fragment containing this node'}
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
          onMouseEnter={() => nodeData.fragmentId && executeFragmentCtx?.setHoveredFragmentId(nodeData.fragmentId)}
          onMouseLeave={() => executeFragmentCtx?.setHoveredFragmentId(null)}
          onClick={e => {
            e.stopPropagation()
            if (nodeData.isPersistent) {
              wsClient.stopPersistentNode(id)
            } else {
              executeFragmentCtx!.executeFragmentForNode(id)
            }
          }}
        >
          <NgolIcon name={nodeData.isPersistent ? 'stop' : 'play'} size={12} />
        </button>
      )}
      {/* ヘッダー */}
      <div className="custom-node-header">
        {category && (
          <span className="custom-node-category">{category}</span>
        )}
        <span className="custom-node-title">{nodeData.label}</span>
        {nodeData.stale && (
          <span
            title="Node definition updated. Re-execute to apply."
            className="ngol-btn-with-icon"
            style={{ marginLeft: 4, background: '#ff9800', borderRadius: 3, padding: '0 4px', fontSize: 9, color: '#fff', verticalAlign: 'middle' }}
          ><NgolIcon name="warning" size={9} className="ngol-icon" /> Re-execute</span>
        )}
        {showVersionMismatchBadge && savedNodeTypeVersion && (
          <NodeVersionMismatchBadge
            savedVersion={savedNodeTypeVersion}
            currentVersion={currentNodeTypeVersion}
          />
        )}
        {/* 型ID上書きウィジェット適用中バッジ（G1） */}
        {activeUi?.kind === 'widget' && activeUi.overrideLabel != null && (
          <span
            title={`WebUI plugin override active: ${activeUi.overrideLabel}`}
            className="ngol-btn-with-icon"
            style={{ marginLeft: 4, background: '#7c4dff', borderRadius: 3, padding: '0 4px', fontSize: 9, color: '#fff', verticalAlign: 'middle' }}
          ><NgolIcon name="edit" size={9} className="ngol-icon" /> Override</span>
        )}
      </div>

      {/* ポート部 */}
      <div className="custom-node-body" style={{ minHeight: bodyHeight }}>
        {/* 入力ポート (左) */}
        <div className="custom-node-ports-col input-col">
          {/* 合成 exec ポート: 実ポートの有無に関わらず常時描画（データを渡さず実行順序だけを繋ぐ用） */}
          <div className="custom-node-port input-port exec-port">
            <Handle
              type="target"
              position={Position.Left}
              id={EXEC_IN_PORT_NAME}
              className="custom-handle input-handle exec-handle"
              title="Execution order (no data) — connect a node here to run this node after it"
            />
          </div>
          {inputPorts.map(port => {
            const dt = port.dataType.toLowerCase()
            const showInline = shouldShowPortInlineEditor(port, inputPorts.length)
            const isBool   = showInline && (dt === 'boolean' || dt === 'bool')
            const isNum    = showInline && (dt === 'number' || dt === 'float' || dt === 'double' || dt === 'int' || dt === 'integer')
            const isString = showInline && dt === 'string'
            const isColor  = showInline && dt === 'color'
            const isEnum   = showInline && dt.startsWith('enum:')
            const enumOptions = isEnum ? port.dataType.slice(5).split('|') : []
            const paramVal = nodeData.paramValues[port.name]
            // boolean: 未設定は false として表示（未送信時のサーバー側デフォルトは false のため）
            const checked = isBool
              ? (paramVal === undefined ? false : paramVal === true || paramVal === 'true')
              : false

            return (
              <div key={port.name} className="custom-node-port input-port">
                <Handle
                  type="target"
                  position={Position.Left}
                  id={port.name}
                  className="custom-handle input-handle"
                  title={`${port.name}: ${port.dataType}`}
                />
                {isBool ? (
                  // boolean ポート: ラベル + チェックボックスをインライン表示
                  <span className="port-label port-label-bool">
                    {port.name}
                    <input
                      type="checkbox"
                      className="node-inline-checkbox"
                      checked={checked}
                      title={port.description ?? port.name}
                      onChange={e => {
                        e.stopPropagation()
                        handleParamChange(port.name, e.target.checked)
                      }}
                      onMouseDown={e => e.stopPropagation()}
                      onPointerDown={e => e.stopPropagation()}
                      onKeyDown={handleInputKeyDown}
                    />
                  </span>
                ) : isNum ? (
                  // number ポート: ラベル + 数値入力をインライン表示
                  <span className="port-label port-label-num">
                    {port.name}
                    <input
                      type="number"
                      className="node-inline-number"
                      value={paramVal === undefined ? '' : String(paramVal)}
                      step="0.01"
                      title={port.description ?? port.name}
                      onChange={e => {
                        e.stopPropagation()
                        const v = e.target.value
                        handleParamChange(port.name, v === '' ? undefined : parseFloat(v))
                      }}
                      onMouseDown={e => e.stopPropagation()}
                      onPointerDown={e => e.stopPropagation()}
                      onKeyDown={handleInputKeyDown}
                    />
                  </span>
                ) : isString ? (
                  // string ポート: ラベル + テキスト入力をインライン表示
                  <span className="port-label port-label-num">
                    {port.name}
                    <input
                      type="text"
                      className="node-inline-string"
                      value={paramVal === undefined ? '' : String(paramVal)}
                      title={port.description ?? port.name}
                      onChange={e => {
                        e.stopPropagation()
                        handleParamChange(port.name, e.target.value)
                      }}
                      onMouseDown={e => e.stopPropagation()}
                      onPointerDown={e => e.stopPropagation()}
                      onKeyDown={handleInputKeyDown}
                    />
                  </span>
                ) : isEnum ? (
                  // enum ポート: ドロップダウン選択
                  <span className="port-label port-label-enum">
                    {port.name}
                    <select
                      className="node-inline-select"
                      value={typeof paramVal === 'string' ? paramVal : (enumOptions[0] ?? '')}
                      title={port.description ?? port.name}
                      onChange={e => {
                        e.stopPropagation()
                        handleParamChange(port.name, e.target.value)
                      }}
                      onMouseDown={e => e.stopPropagation()}
                      onPointerDown={e => e.stopPropagation()}
                      onKeyDown={handleInputKeyDown}
                    >
                      {enumOptions.map(opt => (
                        <option key={opt} value={opt}>{opt}</option>
                      ))}
                    </select>
                  </span>
                ) : isColor ? (
                  // Color ポート: 常にスウォッチを表示（未設定はグレー）
                  <span className="port-label">
                    {port.name}
                    <span
                      className={`port-color-swatch${paramVal == null ? ' port-color-swatch-unset' : ''}`}
                      style={paramVal != null ? { background: rgbaCssColor(toRgbaColor(paramVal)) } : {}}
                      title={paramVal != null
                        ? (() => { const c = toRgbaColor(paramVal); return `rgba(${Math.round(c.r*255)},${Math.round(c.g*255)},${Math.round(c.b*255)},${c.a.toFixed(2)})` })()
                        : 'Color (not set — configurable in Inspector)'
                      }
                    />
                  </span>
                ) : (
                  <span className="port-label">{port.name}</span>
                )}
              </div>
            )
          })}
        </div>

        {/* 出力ポート (右) */}
        <div className="custom-node-ports-col output-col">
          {/* 合成 exec ポート: 実ポートの有無に関わらず常時描画（データを渡さず実行順序だけを繋ぐ用） */}
          <div className="custom-node-port output-port exec-port">
            <Handle
              type="source"
              position={Position.Right}
              id={EXEC_OUT_PORT_NAME}
              className="custom-handle output-handle exec-handle"
              title="Execution order (no data) — connect this to run the next node after this one"
            />
          </div>
          {outputPorts.map(port => {
            const dt = port.dataType.toLowerCase()
            const isEditable = shouldShowPortInlineEditor(port, inputPorts.length) &&
              (dt === 'number' || dt === 'float' || dt === 'double' || dt === 'int' || dt === 'integer' || dt === 'string')
            const paramVal = nodeData.paramValues[port.name]
            const isNum = dt !== 'string'
            return (
              <div key={port.name} className="custom-node-port output-port">
                {isEditable ? (
                  <span className="port-label port-label-num">
                    <input
                      type={isNum ? 'number' : 'text'}
                      className={isNum ? 'node-inline-number' : 'node-inline-string'}
                      value={paramVal === undefined ? '' : String(paramVal)}
                      step={isNum ? '0.01' : undefined}
                      title={port.description ?? port.name}
                      onChange={e => {
                        e.stopPropagation()
                        const v = e.target.value
                        handleParamChange(port.name, isNum ? (v === '' ? undefined : parseFloat(v)) : v)
                      }}
                      onMouseDown={e => e.stopPropagation()}
                      onPointerDown={e => e.stopPropagation()}
                      onKeyDown={handleInputKeyDown}
                    />
                    {port.name}
                  </span>
                ) : (
                  <span className="port-label">{port.name}</span>
                )}
                <Handle
                  type="source"
                  position={Position.Right}
                  id={port.name}
                  className="custom-handle output-handle"
                  title={`${port.name}: ${port.dataType}`}
                />
              </div>
            )
          })}
        </div>
      </div>

      {/* Snapshot PIN チェックボックス */}
      {isSnapshot && (
        <label
          className={`snapshot-pin-row${nodeData.snapshotPinned ? ' pinned' : ''}`}
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
        >
          <input
            type="checkbox"
            checked={nodeData.snapshotPinned ?? false}
            onChange={e => {
              e.stopPropagation()
              wsClient.setSnapshotPin(id, e.target.checked)
            }}
            onClick={e => e.stopPropagation()}
          />
          📌 PIN
        </label>
      )}

      {/* Snapshot ToString チェックボックス (T6) */}
      {isSnapshot && (
        <label
          className="snapshot-tostring-row"
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
        >
          <input
            type="checkbox"
            checked={(nodeData.paramValues['showToString'] as boolean) ?? false}
            onChange={e => {
              e.stopPropagation()
              handleParamChange('showToString', e.target.checked)
            }}
            onClick={e => e.stopPropagation()}
          />
          ToString
        </label>
      )}

      {/* Snapshot allowNull チェックボックス (T12.1) */}
      {isSnapshot && (
        <label
          className="snapshot-allownull-row"
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
        >
          <input
            type="checkbox"
            checked={(nodeData.paramValues['allowNull'] as boolean) ?? false}
            onChange={e => {
              e.stopPropagation()
              handleParamChange('allowNull', e.target.checked)
            }}
            onClick={e => e.stopPropagation()}
          />
          Allow null
        </label>
      )}

      {/* AnySnapshot: 上流型自動検出バッジ */}
      {isAnySnapshot && upstreamDataType && (
        <div className="snapshot-upstream-type-badge">
          {upstreamDataType}
        </div>
      )}

      {/* 統合Snapshot: 上流ポート型バッジ */}
      {isUnifiedSnapshot && upstreamDataType && (
        <div className="snapshot-upstream-type-badge">
          {upstreamDataType}
        </div>
      )}

      {/* 型専用Snapshot: 上流型不一致警告バッジ */}
      {isTypedSnapshot && typeMismatchWarning && (
        <div className="snapshot-type-mismatch-badge ngol-btn-with-icon">
          <NgolIcon name="warning" size={10} className="ngol-icon" /> type mismatch
        </div>
      )}

      {/* 永続実行中バッジ */}
      {nodeData.isPersistent && (
        <div className="persistent-node-badge">⚙ PERSISTENT</div>
      )}

      {/* Snapshot SAVED バッジ */}
      {nodeData.snapshotBadge && (
        <div className="snapshot-saved-badge">
          SAVED · {nodeData.snapshotBadge.valueType} · {nodeData.snapshotBadge.time}
        </div>
      )}

      {/* Snapshot ToString 値表示 (T6) */}
      {isSnapshot && (nodeData.paramValues['showToString'] as boolean) && nodeData.snapshotBadge?.valueString != null && (
        <div className="snapshot-tostring-value">
          {nodeData.snapshotBadge.valueString}
        </div>
      )}

      {/* UI プラグイン ウィジェット (宣言 / 型ID上書き) */}
      {activeUi?.kind === 'widget' && (
        activeUi.overrideLabel != null ? (
          <NodeOverrideErrorBoundary
            nodeTypeId={nodeData.nodeTypeId}
            onError={() => setOverrideFailed(true)}
          >
            <activeUi.Component
              spec={activeUi.spec}
              nodeId={id}
              snapshotBadge={nodeData.snapshotBadge ?? null}
              snapshotBadgesByPort={nodeData.snapshotBadgesByPort}
              paramValues={nodeData.paramValues}
              onParamChange={handleParamChange}
            />
          </NodeOverrideErrorBoundary>
        ) : (
          <PluginErrorBoundary pluginId={String(activeUi.spec.pluginId ?? '')}>
            <activeUi.Component
              spec={activeUi.spec}
              nodeId={id}
              snapshotBadge={nodeData.snapshotBadge ?? null}
              snapshotBadgesByPort={nodeData.snapshotBadgesByPort}
              paramValues={nodeData.paramValues}
              onParamChange={handleParamChange}
            />
          </PluginErrorBoundary>
        )
      )}
    </div>
  )
})
