import type React from 'react'
import type { NodeTypeInfo } from '../types/protocol'

/** ウィジェットコンポーネントに渡される snapshotBadge の型。 */
export interface WidgetSnapshotBadge {
  portName: string
  valueType: string
  time: string
  valueString?: string | null
}

/** ウィジェットコンポーネントの props。 */
export interface WidgetProps {
  spec: Record<string, unknown>
  nodeId: string
  /** 後方互換: このノードが最後に SetSnapshot したポートのバッジ（1件のみ）。 */
  snapshotBadge: WidgetSnapshotBadge | null
  /**
   * このノードの全ポート分のバッジ（ポート名 → バッジ）。
   * 1回の Execute() で複数ポートに SetSnapshot するノード（例: items→selected→index）は
   * snapshotBadge だと最後に書いたポートしか見えないため、こちらでポート名を指定して読むこと。
   */
  snapshotBadgesByPort?: Record<string, WidgetSnapshotBadge>
  paramValues: Record<string, unknown>
  onParamChange: (name: string, value: string) => void
}

/** フルノード描画コンポーネントの props。外枠とポート Handle は NGOL 側が管理する。 */
export interface NodeRendererProps {
  spec: Record<string, unknown>
  nodeId: string
  nodeTypeInfo: NodeTypeInfo
  paramValues: Record<string, unknown>
  onParamChange: (name: string, value: string) => void
  /** 後方互換: このノードが最後に SetSnapshot したポートのバッジ（1件のみ）。 */
  snapshotBadge: WidgetSnapshotBadge | null
  /**
   * このノードの全ポート分のバッジ（ポート名 → バッジ）。
   * 1回の Execute() で複数ポートに SetSnapshot するノードはこちらでポート名を指定して読むこと。
   */
  snapshotBadgesByPort?: Record<string, WidgetSnapshotBadge>
  selected: boolean
  runFragment: () => void
}

/** ポート Handle の縦位置マップ（ポート名 → top px）。 */
export interface PortLayoutResult {
  inputs: Record<string, number>
  outputs: Record<string, number>
}

/** フルノード描画の登録オプション。 */
export interface NodeRendererOptions {
  /** Handle の縦位置を指定する。省略時は既定レイアウト（top 48px から 26px 間隔）。 */
  portLayout?: (nodeTypeInfo: NodeTypeInfo) => PortLayoutResult
}

type WidgetComponent = React.FC<WidgetProps>
type NodeRendererComponent = React.FC<NodeRendererProps>

interface NodeRendererEntry {
  component: NodeRendererComponent
  options: NodeRendererOptions
}

const widgetRegistry = new Map<string, WidgetComponent>()
const nodeRendererRegistry = new Map<string, NodeRendererEntry>()

/**
 * ウィジェット型 WebUI プラグインをレジストリに登録する。
 * @param pluginId C# NodeWebUiAttribute の PluginId と同じ文字列
 * @param component React コンポーネント
 */
export function registerWebUiPlugin(pluginId: string, component: WidgetComponent): void {
  widgetRegistry.set(pluginId, component)
}

/**
 * フルノード描画型 WebUI プラグインを登録する。
 * 同じ pluginId で widget と nodeRenderer の両方が登録されている場合、nodeRenderer を優先する。
 */
export function registerNodeRenderer(
  pluginId: string,
  component: NodeRendererComponent,
  options?: NodeRendererOptions
): void {
  nodeRendererRegistry.set(pluginId, { component, options: options ?? {} })
}

/** customWebUi の解決結果。kind でフルノード / ウィジェットを区別する。 */
export type ResolvedWebUiPlugin =
  | { kind: 'node'; spec: Record<string, unknown>; Component: NodeRendererComponent; options: NodeRendererOptions }
  | { kind: 'widget'; spec: Record<string, unknown>; Component: WidgetComponent }

/**
 * node_list_response の customWebUi JSON 文字列からプラグインを解決する。
 * 解決優先順位: nodeRenderer → widget → null（フォールバック表示）。
 */
export function resolveFromCustomWebUiJson(
  customWebUiJson: string | undefined | null
): ResolvedWebUiPlugin | null {
  if (!customWebUiJson) return null
  try {
    const spec = JSON.parse(customWebUiJson) as Record<string, unknown>
    const pluginId = spec.pluginId as string | undefined
    if (!pluginId) return null

    const nodeEntry = nodeRendererRegistry.get(pluginId)
    if (nodeEntry) return { kind: 'node', spec, Component: nodeEntry.component, options: nodeEntry.options }

    const widget = widgetRegistry.get(pluginId)
    if (widget) return { kind: 'widget', spec, Component: widget }

    return null
  } catch {
    return null
  }
}
