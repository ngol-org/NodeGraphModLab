import { useState, useEffect } from 'react'
import type { Node } from '@xyflow/react'
import { wsClient } from '../lib/wsClient'
import type { CustomNodeData } from '../components/CustomNode'

interface UseGraphEditorSyncParams {
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  setExportResult: React.Dispatch<React.SetStateAction<{ success: boolean; message: string } | null>>
  nodeTypeMap: Map<string, import('../types/protocol').NodeTypeInfo>
}

export function useGraphEditorSync({ setRfNodes, setExportResult, nodeTypeMap }: UseGraphEditorSyncParams) {
  // node_list_updated で通知された型IDを蓄積する。stale フラグそのものの真偽はここでは持たず、
  // 各ノードインスタンスの data.stale が唯一のソースオブトゥルース（下の nodeTypeMap 反映 effect が
  // これを上書きしないよう注意すること。過去に「クリア済みインスタンスが無関係な再描画で
  // stale:true に巻き戻る」不具合を作り込んだため）。
  const [recentlyUpdatedTypeIds, setRecentlyUpdatedTypeIds] = useState<Set<string>>(new Set())
  const [pluginVersion, setPluginVersion] = useState<string>('')
  const [gameName, setGameName] = useState<string>('')
  const [runtimeType, setRuntimeType] = useState<string>('')

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'node_list_updated') {
        const ids = msg.updatedNodeTypeIds ?? []
        if (ids.length === 0) return
        const idSet = new Set(ids)
        // 該当型の現在のインスタンス全てを stale:true にする（この時点で一度だけ行う）。
        setRfNodes(nodes => nodes.map(n => {
          const nData = n.data as CustomNodeData
          if (!idSet.has(nData.nodeTypeId)) return n
          return { ...n, data: { ...nData, stale: true } }
        }))
        setRecentlyUpdatedTypeIds(prev => new Set([...prev, ...ids]))
      } else if (msg.type === 'welcome') {
        setPluginVersion(msg.pluginVersion)
        setGameName(msg.gameName)
        setRuntimeType(msg.runtimeType)
      } else if (msg.type === 'execution_progress' && (msg.status === 'done' || msg.status === 'error')) {
        // 実行が完了/エラー終了したノードインスタンスの「型」の stale を解除する。
        // 同じ型の他インスタンス（未実行・別断片含む）も一緒にクリアしてよい仕様（ユーザー確認済み）。
        // node_list_updated の送出はコンパイル成功時のみ（NgolRuntime.cs）なので、
        // stale バッジが出ている型は実行結果の成否によらず既に新コードが適用済み。
        const { nodeInstanceId } = msg
        setRfNodes(nodes => {
          const target = nodes.find(n => n.id === nodeInstanceId)
          const nodeTypeId = (target?.data as CustomNodeData | undefined)?.nodeTypeId
          if (nodeTypeId === undefined) return nodes
          return nodes.map(n => {
            const nData = n.data as CustomNodeData
            if (nData.nodeTypeId !== nodeTypeId || !nData.stale) return n
            return { ...n, data: { ...nData, stale: false } }
          })
        })
      } else if (msg.type === 'export_nodes_response') {
        setExportResult({
          success: msg.success,
          message: msg.success
            ? `Exported: ${msg.savedPath ?? ''}`
            : (msg.errorMessage ?? 'Export failed'),
        })
      }
    })
    return unsub
  }, [setRfNodes, setExportResult])

  // nodeTypeMap 更新時: 直近ホットリロードされた型の nodeTypeInfo を最新に反映する。
  // stale フラグには一切触れない（stale の設定/解除は上のメッセージハンドラのみが担当する）。
  // 型がレジストリから消えていた場合（純粋カスタムノード削除など）は removed 状態に昇格させ、
  // その場合のみ stale も明示的に false にする（削除済みノードにバッジは出さない）。
  useEffect(() => {
    if (recentlyUpdatedTypeIds.size === 0) return
    setRfNodes(nodes => nodes.map(n => {
      const nData = n.data as CustomNodeData
      if (!recentlyUpdatedTypeIds.has(nData.nodeTypeId)) return n
      const newTypeInfo = nodeTypeMap.get(nData.nodeTypeId)
      if (newTypeInfo === undefined) {
        return { ...n, data: { ...nData, stale: false, removed: true } }
      }
      if (nData.nodeTypeInfo === newTypeInfo && nData.removed !== true) return n
      return { ...n, data: { ...nData, nodeTypeInfo: newTypeInfo, removed: false } }
    }))
  }, [nodeTypeMap, recentlyUpdatedTypeIds, setRfNodes])

  return { pluginVersion, gameName, runtimeType }
}
