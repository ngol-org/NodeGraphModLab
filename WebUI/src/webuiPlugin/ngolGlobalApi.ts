import React from 'react'
import htm from 'htm'
import { RgbaColorPicker } from 'react-colorful'
import { useReactFlow } from '@xyflow/react'
import { wsClient } from '../lib/wsClient'
import { registerWebUiPlugin, registerNodeRenderer } from './webuiPluginRegistry'
import type { WidgetProps, NodeRendererProps, NodeRendererOptions } from './webuiPluginRegistry'
import { registerPluginPanel } from './pluginPanelRegistry'
import { registerNodeTypeOverride } from './nodeTypeOverrideRegistry'
import { registerCategoryColor } from './categoryColorRegistry'
import {
  registerPluginMenu,
  registerNodeContextMenuItemsProvider,
  registerPluginOverlay,
} from './pluginExtensionRegistry'
import { initPluginEventBus, onPluginEvent } from './pluginEventBus'
import type { ServerMessage } from '../types/protocol'
import { autoPortLayout, PORT_BASE, PORT_STEP, EXEC_TOP } from '../lib/nodePortLayout'

/**
 * window.NGOL の API バージョン。
 * window.NGOL の破壊的変更時にインクリメントする。
 */
export const NGOL_API_VERSION = 1

/**
 * 外部プラグイン .js に公開するグローバル API を window.NGOL として構築する。
 * 必ず外部プラグインの dynamic import より前に呼ぶこと。
 *
 * React を本体と同一インスタンスで共有させるのが最重要ポイント
 * （プラグインが React を自前で持つと hooks が壊れる）。
 */
export function installNgolGlobalApi(): void {
  initPluginEventBus()
  const html = htm.bind(React.createElement)

  const ngol = {
    apiVersion: NGOL_API_VERSION,

    // ---- ライブラリ提供 ----
    React,
    html,
    // react-colorful の RGBA ピッカー本体。プラグイン側で自前バンドル不要にするため
    // 本体の依存を共有インスタンスとして公開する（React と同じ考え方）。
    RgbaColorPicker,

    // ---- 登録 API（Phase 1）----
    registerWidget: registerWebUiPlugin,
    registerNodeRenderer,
    registerPanel: registerPluginPanel,
    // ノードカテゴリのアクセントカラーを登録する（未登録カテゴリは既定色にフォールバック）。
    registerCategoryColor,

    // ---- ノード型 ID 単位の描画上書き ----
    // [NodeWebUi] 宣言のない任意のノード型（ビルトイン・第三者 .cs）の描画を上書きする。
    // 上書き適用中はノードに Override バッジが表示され、Plugins メニューから標準 UI に戻せる。
    registerNodeRendererOverride: (
      nodeTypeId: string,
      component: React.FC<NodeRendererProps>,
      options?: NodeRendererOptions & { label?: string }
    ) => {
      const { label, ...rendererOptions } = options ?? {}
      registerNodeTypeOverride(nodeTypeId, {
        kind: 'node',
        component,
        options: rendererOptions,
        label: label ?? nodeTypeId,
      })
    },
    registerWidgetOverride: (
      nodeTypeId: string,
      component: React.FC<WidgetProps>,
      options?: { label?: string }
    ) => {
      registerNodeTypeOverride(nodeTypeId, {
        kind: 'widget',
        component,
        options: {},
        label: options?.label ?? nodeTypeId,
      })
    },

    // ---- 通信（wsClient の薄いラッパ）----
    ws: {
      send: (data: object) => wsClient.send(data),
      onMessage: (cb: (msg: ServerMessage) => void) => wsClient.onMessage(cb),
      onConnection: (cb: (connected: boolean) => void) => wsClient.onConnection(cb),
    },

    // ---- スナップショット直接書き込み ----
    // UI プラグインで決定した値を、対象ノードの断片を実行せずに後続断片へ渡す。
    // value は string / number / boolean / null のみ。PIN 中は success=false が返る。
    // 注意: ノードの Execute は走らない（副作用・出力ポート実値は変化しない）。
    setSnapshotValue: (nodeInstanceId: string, portName: string, value: string | number | boolean | null) => {
      wsClient.send({ type: 'set_snapshot_value', nodeInstanceId, portName, value })
    },

    // ---- 永続ノードのライブパラメータ書き込み ----
    // Execute / runFragment なしで params を部分マージする。永続ノードは GetLiveParam で opt-in 読み取り。
    pushLiveParams: (nodeInstanceId: string, params: Record<string, string | number | boolean | null>) => {
      wsClient.send({ type: 'push_node_live_params', nodeInstanceId, params })
    },

    // ---- ユーティリティ ----
    log: (...args: unknown[]) => console.log('[NGOL Plugin]', ...args),

    // ---- nodeRenderer の Handle 座標計算ヘルパー（Phase 3）----
    // portLayout は指定すると既定の自動スプレッド配置を丸ごと置き換えるため、
    // 一部のポートだけ調整したい場合はまず autoPortLayout() で既定値を取得してから上書きする:
    //   portLayout: (t) => ({ ...NGOL.layout.autoPortLayout(t), outputs: { value: 128 } })
    layout: {
      autoPortLayout,
      PORT_BASE,  // データポートの既定開始位置(px)。EXEC_TOP(48)と衝突するため使わないこと
      PORT_STEP,  // ポート間隔(px)
      EXEC_TOP,   // exec_in/exec_out 専用の固定座標(px)。portLayoutでは変更不可
    },

    // ---- エディタ拡張ポイント（Phase 2）----
    extensions: {
      registerMenu: registerPluginMenu,
      registerNodeContextMenuItems: registerNodeContextMenuItemsProvider,
      registerOverlay: registerPluginOverlay,
      // オーバーレイのビューポート追従用（ReactFlow コンテキスト内でのみ使用可）
      useReactFlow,
    },
    events: {
      on: onPluginEvent,
    },
  }

  ;(window as unknown as { NGOL: typeof ngol }).NGOL = ngol
}

/** 外部プラグイン向けグローバル API の型（プラグイン作者ドキュメント用）。 */
export type NgolGlobalApi = ReturnType<typeof buildTypeOnly>
function buildTypeOnly() {
  return (window as unknown as { NGOL: unknown }).NGOL
}
