import type { NodeTypeInfo } from '../types/protocol'

/**
 * nodeRenderer プラグイン: 標準ノード header + body(exec行) と Handle top を揃えるための定数。
 * CustomNode.tsx の標準描画とここでの計算が常に一致するよう、値の定義箇所はここ1箇所のみにする。
 */
const HEADER_H = 37
const BODY_PAD = 4
const EXEC_ROW_H = 14

/** ポート間隔(px)。`autoPortLayout` が返す各ポートの間隔もこれに揃う。 */
export const PORT_STEP = 26
/** exec_in/exec_out 専用の固定座標(px)。portLayout では変更できない。データポートに使うと重なる。 */
export const EXEC_TOP = HEADER_H + BODY_PAD + EXEC_ROW_H / 2
/** データポートの既定開始位置(px)。`portLayout` で明示しないポートはこの値にフォールバックする。 */
export const PORT_BASE = HEADER_H + BODY_PAD + EXEC_ROW_H + 2 + 11

export interface PortLayoutResult {
  inputs: Record<string, number>
  outputs: Record<string, number>
}

/**
 * 既定の自動スプレッド配置（PORT_BASE 起点・PORT_STEP 間隔）を計算する。
 * `portLayout` オプションは指定すると既定配置を丸ごと置き換えるため、一部のポートだけ
 * 手動調整したいプラグイン作者はこれをベースに必要な箇所だけ上書きするとよい:
 *
 * ```js
 * portLayout: (nodeTypeInfo) => ({
 *   ...NGOL.layout.autoPortLayout(nodeTypeInfo),
 *   outputs: { value: 128 }, // value だけ上書き
 * })
 * ```
 */
export function autoPortLayout(nodeTypeInfo: NodeTypeInfo): PortLayoutResult {
  const inputs: Record<string, number> = {}
  const outputs: Record<string, number> = {}
  nodeTypeInfo.ports
    .filter(p => p.direction === 'input')
    .forEach((p, i) => { inputs[p.name] = PORT_BASE + i * PORT_STEP })
  nodeTypeInfo.ports
    .filter(p => p.direction === 'output')
    .forEach((p, i) => { outputs[p.name] = PORT_BASE + i * PORT_STEP })
  return { inputs, outputs }
}
