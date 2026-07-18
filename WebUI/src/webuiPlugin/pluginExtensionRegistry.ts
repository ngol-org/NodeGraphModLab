import type React from 'react'

// ────────────────────────────────────────────────────────────────
// エディタ拡張ポイントのレジストリ
// widget / nodeRenderer / panel（Phase 1・render 前登録の静的レジストリ）と異なり、
// render 後の遅延登録にも UI が追従できるよう useSyncExternalStore 対応の
// 変更通知付きストアとして実装する。スナップショットは immutable 配列差し替え方式。
// ────────────────────────────────────────────────────────────────

/** メニューバー拡張メニューの項目。 */
export type PluginMenuItem =
  | { label: string; onClick: () => void; separator?: false }
  | { separator: true }

/** メニューバー拡張メニュー。 */
export interface PluginMenuDef {
  label: string
  items: PluginMenuItem[]
}

/** ノードコンテキストメニュー拡張のコールバックに渡されるノード情報。 */
export interface NodeContextMenuContext {
  nodeId: string
  nodeTypeId: string
  paramValues: Record<string, unknown>
  isPersistentRunning: boolean
}

/** ノードコンテキストメニュー拡張項目。 */
export interface PluginContextMenuItem {
  label: string
  onClick: () => void
}

/** ノード種別ごとの出し分けはコールバック側で行う（対象外なら空配列を返す）。 */
export type NodeContextMenuItemsProvider = (ctx: NodeContextMenuContext) => PluginContextMenuItem[]

/** キャンバスオーバーレイ。component は ReactFlow の子として描画される。 */
export interface PluginOverlayDef {
  id: string
  component: React.FC
}

interface ExtensionState {
  menus: readonly PluginMenuDef[]
  contextMenuProviders: readonly NodeContextMenuItemsProvider[]
  overlays: readonly PluginOverlayDef[]
}

let state: ExtensionState = { menus: [], contextMenuProviders: [], overlays: [] }
const listeners = new Set<() => void>()

function notify(): void {
  listeners.forEach(l => { try { l() } catch { /* noop */ } })
}

/** useSyncExternalStore 用 subscribe。 */
export function subscribeExtensions(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** useSyncExternalStore 用 getSnapshot（参照安定・変更時のみ差し替え）。 */
export function getExtensionSnapshot(): ExtensionState {
  return state
}

/** メニューバー拡張メニューを登録する。同一 label は上書き。 */
export function registerPluginMenu(def: PluginMenuDef): void {
  const rest = state.menus.filter(m => m.label !== def.label)
  state = { ...state, menus: [...rest, def] }
  notify()
}

/** ノードコンテキストメニュー拡張 provider を登録する。 */
export function registerNodeContextMenuItemsProvider(provider: NodeContextMenuItemsProvider): void {
  state = { ...state, contextMenuProviders: [...state.contextMenuProviders, provider] }
  notify()
}

/** キャンバスオーバーレイを登録する。同一 id は上書き。 */
export function registerPluginOverlay(def: PluginOverlayDef): void {
  const rest = state.overlays.filter(o => o.id !== def.id)
  state = { ...state, overlays: [...rest, def] }
  notify()
}

/**
 * 全 provider を評価して拡張コンテキストメニュー項目を集める。
 * provider の例外は捕捉し、該当 provider のみスキップする。
 */
export function collectContextMenuItems(ctx: NodeContextMenuContext): PluginContextMenuItem[] {
  const items: PluginContextMenuItem[] = []
  for (const provider of state.contextMenuProviders) {
    try {
      items.push(...provider(ctx))
    } catch (e) {
      console.warn('[NGOL Plugin] context menu provider error:', e)
    }
  }
  return items
}
