import type { Edge } from '@xyflow/react'
import type { NodeTypeInfo } from '../types/protocol'

/** ドラッグを開始したハンドルの情報。handleType でドラッグ元が出力/入力どちらかを判別する */
export interface PendingConnection {
  nodeId: string
  handleId: string
  handleType: 'source' | 'target'
}

/**
 * データを持たず実行順序のみを表す予約ポート名。
 * CustomNode.tsx が実ポートの有無に関わらず全ノードへ常時描画する合成ハンドルの id。
 */
export const EXEC_IN_PORT_NAME = '__exec_in__'
export const EXEC_OUT_PORT_NAME = '__exec_out__'

export function getFirstInputPortName(nodeTypeInfo?: NodeTypeInfo): string | null {
  return nodeTypeInfo?.ports.find(port => port.direction === 'input')?.name ?? null
}

export function getFirstOutputPortName(nodeTypeInfo?: NodeTypeInfo): string | null {
  return nodeTypeInfo?.ports.find(port => port.direction === 'output')?.name ?? null
}

/**
 * 出力ポート/入力ポートいずれからのドラッグ&ドロップでも、新規ノードとの自動接続エッジを生成する。
 * - handleType が 'source'（出力ポートからドラッグ）: pending → 新規ノードの最初の入力ポート
 * - handleType が 'target'（入力ポートからドラッグ）: 新規ノードの最初の出力ポート → pending
 * - ドラッグ元が合成 exec ポートそのものだった場合は、新規ノードの実ポートを無視して
 *   常に新規ノード側の exec ポートへ接続する（ユーザーがデータではなく実行順序だけを繋ぐ意図と判断）
 */
export function buildAutoConnectEdge(
  pending: PendingConnection | null,
  newNodeId: string,
  nodeTypeInfo?: NodeTypeInfo,
): Edge | null {
  if (!pending) return null

  if (pending.handleType === 'source') {
    const targetHandle = pending.handleId === EXEC_OUT_PORT_NAME
      ? EXEC_IN_PORT_NAME
      : (getFirstInputPortName(nodeTypeInfo) ?? EXEC_IN_PORT_NAME)
    return {
      id: `edge-${crypto.randomUUID()}`,
      source: pending.nodeId,
      sourceHandle: pending.handleId,
      target: newNodeId,
      targetHandle,
      animated: true,
    }
  }

  const sourceHandle = pending.handleId === EXEC_IN_PORT_NAME
    ? EXEC_OUT_PORT_NAME
    : (getFirstOutputPortName(nodeTypeInfo) ?? EXEC_OUT_PORT_NAME)
  return {
    id: `edge-${crypto.randomUUID()}`,
    source: newNodeId,
    sourceHandle,
    target: pending.nodeId,
    targetHandle: pending.handleId,
    animated: true,
  }
}
