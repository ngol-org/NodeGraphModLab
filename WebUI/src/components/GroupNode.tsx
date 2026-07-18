import { memo, useState, useRef, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Handle, Position } from '@xyflow/react'
import { NgolIcon } from './icons/NgolIcon'
import './GroupNode.css'

export interface GroupNodeData extends Record<string, unknown> {
  groupId: string
  name: string
  description?: string
  memberCount: number
  collapsed: boolean
  color?: string
  /** 展開時の背景矩形の幅（px） */
  width?: number
  /** 展開時の背景矩形の高さ（px） */
  height?: number
  /** 折りたたみトグルコールバック（GroupNode 内ボタン用） */
  onToggleCollapsed: (groupId: string) => void
  /** グループ名変更コールバック（新名前を受け取る） */
  onRename: (groupId: string, newName: string) => void
  /** グループ解除コールバック（右クリックメニューから呼ぶ用・UI上のボタンなし） */
  onDissolve: (groupId: string) => void
}

export const GroupNode = memo(function GroupNode({ data }: { data: GroupNodeData }) {
  const {
    groupId,
    name,
    description,
    memberCount,
    collapsed,
    color = '#4a9eff',
    width = 200,
    height = 100,
    onToggleCollapsed,
    onRename,
  } = data

  const [isRenaming, setIsRenaming] = useState(false)
  const [renamingValue, setRenamingValue] = useState(name)
  const [tooltipPos, setTooltipPos] = useState<{ x: number; y: number } | null>(null)
  const headerRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const renamingValueRef = useRef(renamingValue)
  renamingValueRef.current = renamingValue

  const commitRename = useCallback(() => {
    const trimmed = renamingValueRef.current.trim()
    if (trimmed && trimmed !== name) onRename(groupId, trimmed)
    setIsRenaming(false)
  }, [groupId, name, onRename])

  // document レベルの mousemove でヘッダー内外を判定（React Flow のイベント干渉を完全回避）
  useEffect(() => {
    if (!description) return
    let timer: ReturnType<typeof setTimeout> | null = null
    let isOver = false
    const onMove = (e: MouseEvent) => {
      const rect = headerRef.current?.getBoundingClientRect()
      if (!rect) return
      const inside = e.clientX >= rect.left && e.clientX <= rect.right &&
                     e.clientY >= rect.top  && e.clientY <= rect.bottom
      if (inside && !isOver) {
        isOver = true
        timer = setTimeout(() => {
          const r = headerRef.current?.getBoundingClientRect()
          if (r && r.width > 0) setTooltipPos({ x: r.left, y: r.bottom + 4 })
        }, 600)
      } else if (!inside && isOver) {
        isOver = false
        if (timer) { clearTimeout(timer); timer = null }
        setTooltipPos(null)
      }
    }
    document.addEventListener('mousemove', onMove)
    return () => {
      document.removeEventListener('mousemove', onMove)
      if (timer) clearTimeout(timer)
      setTooltipPos(null)
    }
  }, [description, collapsed])

  // isRenaming になったら外部クリック(mousedown)で確定
  // ダブルクリックの各イベント(~300ms)が終わってから登録する
  useEffect(() => {
    if (!isRenaming) return
    setRenamingValue(name)
    const handleOutside = (e: MouseEvent) => {
      if (inputRef.current && !inputRef.current.contains(e.target as Element)) {
        commitRename()
      }
    }
    const timer = setTimeout(() => {
      document.addEventListener('mousedown', handleOutside)
    }, 250)
    return () => {
      clearTimeout(timer)
      document.removeEventListener('mousedown', handleOutside)
    }
  }, [isRenaming]) // eslint-disable-line react-hooks/exhaustive-deps

  /** ヘッダーまたは折りたたみコンテンツのダブルクリックでリネーム開始 */
  const handleStartRename = (e: React.MouseEvent) => {
    e.stopPropagation()
    e.nativeEvent.stopPropagation()  // ReactFlow のネイティブイベントハンドラも止める
    e.preventDefault()
    setIsRenaming(true)
  }

  const nameInput = (
    <input
      ref={inputRef}
      autoFocus
      className="group-node-rename-input"
      value={renamingValue}
      onChange={e => setRenamingValue(e.target.value)}
      onKeyDown={e => {
        e.stopPropagation()
        if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); commitRename() }
        if (e.key === 'Escape') setIsRenaming(false)
      }}
      onMouseDown={e => e.stopPropagation()}
      onClick={e => e.stopPropagation()}
      onDoubleClick={e => e.stopPropagation()}
    />
  )

  const nameLabel = (
    <span className="group-node-name" title="Double-click to rename">{name}</span>
  )

  const tooltip = tooltipPos && description
    ? createPortal(
        <div className="group-node-tooltip" style={{ position: 'fixed', left: tooltipPos.x, top: tooltipPos.y }}>{description}</div>,
        document.body
      )
    : null

  if (collapsed) {
    return (
      <div
        className="group-node group-node-collapsed"
        style={{ borderColor: color }}
      >
        {/* エッジルーティング用。isConnectable=false で手動接続は不可 */}
        <Handle type="target" position={Position.Left} id="group-in" isConnectable={false} className="group-collapsed-handle" style={{ opacity: 0, pointerEvents: 'none' }} />
        <Handle type="source" position={Position.Right} id="group-out" isConnectable={false} className="group-collapsed-handle" style={{ opacity: 0, pointerEvents: 'none' }} />
        <div
          ref={headerRef}
          className="group-node-collapsed-content"
          onDoubleClick={handleStartRename}
        >
          <button
            className="group-node-toggle"
            onClick={e => { e.stopPropagation(); onToggleCollapsed(groupId) }}
            onDoubleClick={e => e.stopPropagation()}
            title="Expand"
          >
            <NgolIcon name="chevron-right" size={12} />
          </button>
          {isRenaming ? nameInput : nameLabel}
          <span className="group-node-badge">🗂 {memberCount}</span>
          {tooltip}
        </div>
      </div>
    )
  }

  // 展開時: 背景矩形
  // ヘッダー全体がダブルクリックでリネーム開始・ドラッグ対象
  // ボディの mousedown は stopPropagation でキャンバス範囲選択を防止
  return (
    <div
      className="group-node group-node-expanded"
      style={{ width, height, borderColor: color }}
    >
      <div
        ref={headerRef}
        className="group-drag-handle group-node-header"
        onDoubleClick={handleStartRename}
      >
        <button
          className="group-node-toggle"
          onClick={e => { e.stopPropagation(); onToggleCollapsed(groupId) }}
          onDoubleClick={e => e.stopPropagation()}
          title="Collapse"
        >
          <NgolIcon name="chevron-down" size={12} />
        </button>
        {isRenaming ? nameInput : nameLabel}
        {description && <span className="group-node-desc-indicator" title={description}>📋</span>}
        {tooltip}
      </div>
      {/* nodrag nopan: ノードドラッグ無効化 + キャンバスパン/範囲選択を防ぐ */}
      <div
        className="group-node-body nodrag nopan"
        onMouseDown={e => { e.stopPropagation(); e.nativeEvent.stopImmediatePropagation() }}
      />
    </div>
  )
})
