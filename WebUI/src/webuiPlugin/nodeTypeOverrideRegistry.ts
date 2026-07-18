import type React from 'react'
import type { WidgetProps, NodeRendererProps, NodeRendererOptions } from './webuiPluginRegistry'

// ────────────────────────────────────────────────────────────────
// ノード型 ID 単位の描画上書きレジストリ
// [NodeWebUi] を宣言していない任意のノード型（ビルトイン・第三者 .cs 問わず）に対し、
// 外部プラグイン JS が nodeTypeId 指定でウィジェット / フルノード描画を上書きできる。
//
// ガードレール（設計 §2）:
//   G2: disabled セットに入れた型は resolve が常に null（標準 UI に戻すトグル）
//   G4: 同一型への再登録は後勝ち + console.warn（両ラベル明記）
// G1（バッジ）/ G3（失敗時フォールバック）は CustomNode.tsx 側で実装する。
//
// pluginExtensionRegistry と同じ useSyncExternalStore 対応の変更通知付きストア
// （immutable スナップショット差し替え方式）。Plugins メニューのトグル操作を
// 全 CustomNode インスタンスへ再描画として伝播させるために必要。
// ────────────────────────────────────────────────────────────────

export type NodeTypeOverrideKind = 'node' | 'widget'

/** ノード型上書きの登録エントリ。 */
export interface NodeTypeOverrideEntry {
  kind: NodeTypeOverrideKind
  /** kind='node' なら NodeRendererProps、kind='widget' なら WidgetProps を受け取る。 */
  component: React.FC<NodeRendererProps> | React.FC<WidgetProps>
  /** kind='node' の場合のみ使用（portLayout 等）。 */
  options: NodeRendererOptions
  /** 表示・ログ用ラベル（登録元プラグインの識別。省略時は nodeTypeId）。 */
  label: string
}

interface OverrideState {
  overrides: ReadonlyMap<string, NodeTypeOverrideEntry>
  /** G2: 標準 UI に戻すトグルで無効化された型 ID（セッション内のみ・グラフ非保存）。 */
  disabled: ReadonlySet<string>
}

let state: OverrideState = { overrides: new Map(), disabled: new Set() }
const listeners = new Set<() => void>()

function notify(): void {
  listeners.forEach(l => { try { l() } catch { /* noop */ } })
}

/** useSyncExternalStore 用 subscribe。 */
export function subscribeNodeTypeOverrides(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** useSyncExternalStore 用 getSnapshot（参照安定・変更時のみ差し替え）。 */
export function getNodeTypeOverrideSnapshot(): OverrideState {
  return state
}

/**
 * ノード型 ID 上書きを登録する。
 * 同一 nodeTypeId への再登録は後勝ちで上書きし、console.warn で両者を報告する（G4）。
 */
export function registerNodeTypeOverride(nodeTypeId: string, entry: NodeTypeOverrideEntry): void {
  const existing = state.overrides.get(nodeTypeId)
  if (existing) {
    console.warn(
      `[NGOL Plugin] Node type override conflict for '${nodeTypeId}': ` +
      `'${existing.label}' is replaced by '${entry.label}' (last registration wins)`
    )
  }
  const next = new Map(state.overrides)
  next.set(nodeTypeId, entry)
  state = { ...state, overrides: next }
  notify()
}

/**
 * スナップショットから上書きエントリを解決する。
 * disabled（G2 トグル OFF）の型は null を返す。
 * CustomNode からは useSyncExternalStore で得たスナップショットを渡すこと
 * （モジュール変数を直接読むと React の再描画整合が保証されない）。
 */
export function resolveNodeTypeOverride(
  snapshot: OverrideState,
  nodeTypeId: string
): NodeTypeOverrideEntry | null {
  if (snapshot.disabled.has(nodeTypeId)) return null
  return snapshot.overrides.get(nodeTypeId) ?? null
}

/** G2: 型 ID 単位で上書きを無効化 / 再有効化する（Plugins メニューのトグルから呼ぶ）。 */
export function setNodeTypeOverrideDisabled(nodeTypeId: string, disabled: boolean): void {
  if (state.disabled.has(nodeTypeId) === disabled) return
  const next = new Set(state.disabled)
  if (disabled) next.add(nodeTypeId)
  else next.delete(nodeTypeId)
  state = { ...state, disabled: next }
  notify()
}

/** テスト専用: レジストリを初期状態に戻す。プロダクションコードから呼ばないこと。 */
export function __resetNodeTypeOverridesForTest(): void {
  state = { overrides: new Map(), disabled: new Set() }
  listeners.clear()
}
