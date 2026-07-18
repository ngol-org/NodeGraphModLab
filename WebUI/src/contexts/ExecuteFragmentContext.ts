import { createContext, useContext } from 'react'

/** T13/T14: 断片実行コンテキスト — CustomNode から GraphEditorLayout の実行関数を呼ぶ */
export interface ExecuteFragmentContextValue {
  /** ノード ID が属する断片を実行する */
  executeFragmentForNode: (nodeId: string) => void
  /** T14: Run ボタンがホバーされている断片 ID（なければ null） */
  hoveredFragmentId: string | null
  /** T14: ホバー状態をセットする */
  setHoveredFragmentId: (id: string | null) => void
  /** ノードを単体選択状態にする（Run ボタン表示のため） */
  selectNode: (nodeId: string) => void
}

export const ExecuteFragmentContext = createContext<ExecuteFragmentContextValue | null>(null)

export function useExecuteFragment() {
  return useContext(ExecuteFragmentContext)
}
