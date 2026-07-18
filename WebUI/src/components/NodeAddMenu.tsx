import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import type { NodeTypeInfo } from '../types/protocol'
import { NgolIcon } from './icons/NgolIcon'
import { groupAndSortNodes, type NodeSortMode } from '../lib/nodeSort'
import { NodeSortMenu } from './NodeSortMenu'
import './NodeAddMenu.css'

/** fuzzy match: query の各文字が text 内に順番に存在するか */
function fuzzyMatch(text: string, query: string): boolean {
  let ti = 0
  for (let qi = 0; qi < query.length; qi++) {
    while (ti < text.length && text[ti] !== query[qi]) ti++
    if (ti >= text.length) return false
    ti++
  }
  return true
}

/** 単一クエリに対してノードのマッチスコアを返す（高いほど優先） */
function scoreNode(n: NodeTypeInfo, q: string): number {
  const dn = n.displayName.toLowerCase()
  if (dn === q) return 100
  if (dn.startsWith(q)) return 50
  if (dn.includes(q)) return 30
  if (fuzzyMatch(dn, q)) return 10
  return 5
}

interface Props {
  nodeTypes: NodeTypeInfo[]
  position: { x: number; y: number }
  onAdd: (typeId: string) => void
  onClose: () => void
}

export function NodeAddMenu({ nodeTypes, position, onAdd, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [selectedIndex, setSelectedIndex] = useState(-1)
  const [sortMode, setSortMode] = useState<NodeSortMode>('category')
  const inputRef = useRef<HTMLInputElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const selectedItemRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  // クエリ変更時に選択をリセット
  useEffect(() => {
    setSelectedIndex(-1)
  }, [query])

  // 選択アイテムをスクロールして表示
  useEffect(() => {
    selectedItemRef.current?.scrollIntoView({ block: 'nearest' })
  }, [selectedIndex])

  // Escapeで閉じる / クリックアウトで閉じる
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    const handleClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) onClose()
    }
    window.addEventListener('keydown', handleKey)
    window.addEventListener('mousedown', handleClick)
    return () => {
      window.removeEventListener('keydown', handleKey)
      window.removeEventListener('mousedown', handleClick)
    }
  }, [onClose])

  // クエリによる絞り込み＋（クエリありの場合の）関連度スコア降順ソート
  const searched = useCallback(() => {
    const q = query.toLowerCase().trim()
    if (!q) return nodeTypes

    const dn = (n: NodeTypeInfo) => n.displayName.toLowerCase()
    const otherFields = (n: NodeTypeInfo) => [
      n.category.toLowerCase(),
      n.id.toLowerCase(),
      (n.description ?? '').toLowerCase(),
    ]

    const tokens = q.split(/\s+/)

    if (tokens.length > 1) {
      // スペース区切り → AND 検索（各トークンがいずれかのフィールドに含まれる、順序不問）
      return nodeTypes.filter(n =>
        tokens.every(token =>
          dn(n).includes(token) || otherFields(n).some(t => t.includes(token))
        )
      )
    }

    // 単一トークン: displayName は includes + fuzzy、他フィールドは includes のみ、スコア降順
    const result = nodeTypes.filter(n =>
      dn(n).includes(q) || fuzzyMatch(dn(n), q) ||
      otherFields(n).some(t => t.includes(q))
    )
    return [...result].sort((a, b) => scoreNode(b, q) - scoreNode(a, q))
  }, [nodeTypes, query])()

  // 表示グループ（category モードはカテゴリ名のみアルファベット順にし searched の並びを維持、name-asc/desc はフラット化）
  const groups = useMemo(() => groupAndSortNodes(searched, sortMode), [searched, sortMode])
  const flatOrdered = useMemo(() => groups.flatMap(g => g.nodes), [groups])

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setSelectedIndex(i => Math.min(i + 1, flatOrdered.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setSelectedIndex(i => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      if (selectedIndex >= 0 && selectedIndex < flatOrdered.length) {
        onAdd(flatOrdered[selectedIndex].id)
      } else if (flatOrdered.length === 1) {
        onAdd(flatOrdered[0].id)
      }
    }
  }

  return (
    <div
      ref={menuRef}
      className="node-add-menu"
      style={{ left: position.x, top: position.y }}
    >
      <div className="node-add-menu__header">
        <span>🔍 Add Node</span>
        <button className="node-add-menu__close" onClick={onClose} title="Close"><NgolIcon name="close" size={11} /></button>
      </div>
      <div className="node-add-menu__search-row">
        <input
          ref={inputRef}
          className="node-add-menu__input"
          placeholder="Search nodes… (↑↓ to select, Enter to add)"
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
        />
        <NodeSortMenu mode={sortMode} onChange={setSortMode} />
      </div>
      <div className="node-add-menu__list">
        {groups.map(group => (
          <div key={group.category ?? '__flat__'} className="node-add-menu__group">
            {group.category && <div className="node-add-menu__group-label">{group.category}</div>}
            {group.nodes.map(t => {
              const isSelected = flatOrdered[selectedIndex]?.id === t.id
              return (
                <button
                  key={t.id}
                  ref={isSelected ? selectedItemRef : undefined}
                  className={`node-add-menu__item${isSelected ? ' node-add-menu__item--selected' : ''}`}
                  onClick={() => onAdd(t.id)}
                  title={t.description ?? t.id}
                >
                  <span className="node-add-menu__item-name">{t.displayName}</span>
                  <span className="node-add-menu__item-id">{t.id}</span>
                </button>
              )
            })}
          </div>
        ))}
        {flatOrdered.length === 0 && (
          <div className="node-add-menu__empty">No nodes found</div>
        )}
      </div>
    </div>
  )
}
