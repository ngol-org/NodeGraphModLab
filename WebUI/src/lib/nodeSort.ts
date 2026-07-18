import type { NodeTypeInfo } from '../types/protocol'

export type NodeSortMode = 'category' | 'name-asc' | 'name-desc' | 'modified-asc' | 'modified-desc'

export interface NodeGroup {
  /** null はグループ見出しを表示しないフラットリストを意味する */
  category: string | null
  nodes: NodeTypeInfo[]
}

/**
 * name-asc/name-desc はカテゴリ分けを解除しフラットな1グループにする。
 * category は入力順（呼び出し側で検索スコア順・アルファベット順等を決めておく）を保ったままカテゴリ名のみアルファベット順に並べる。
 */
export function groupAndSortNodes(nodes: NodeTypeInfo[], mode: NodeSortMode): NodeGroup[] {
  if (mode === 'name-asc' || mode === 'name-desc') {
    const sorted = [...nodes].sort((a, b) =>
      mode === 'name-asc'
        ? a.displayName.localeCompare(b.displayName)
        : b.displayName.localeCompare(a.displayName)
    )
    return [{ category: null, nodes: sorted }]
  }

  if (mode === 'modified-asc' || mode === 'modified-desc') {
    // lastModified が無い（DLL経由の）ノードは常に末尾へ寄せ、その中はdisplayName順にする
    const withDate = nodes.filter(n => n.lastModified)
    const withoutDate = nodes.filter(n => !n.lastModified)
    withDate.sort((a, b) => {
      const ta = new Date(a.lastModified!).getTime()
      const tb = new Date(b.lastModified!).getTime()
      return mode === 'modified-asc' ? ta - tb : tb - ta
    })
    withoutDate.sort((a, b) => a.displayName.localeCompare(b.displayName))
    return [{ category: null, nodes: [...withDate, ...withoutDate] }]
  }

  const map = new Map<string, NodeTypeInfo[]>()
  for (const n of nodes) {
    const list = map.get(n.category) ?? []
    list.push(n)
    map.set(n.category, list)
  }
  return [...map.keys()].sort().map(category => ({ category, nodes: map.get(category) ?? [] }))
}
