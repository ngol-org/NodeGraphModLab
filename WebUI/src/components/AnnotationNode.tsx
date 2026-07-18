import { memo, useState, useRef, useEffect, useCallback } from 'react'
import { NgolIcon } from './icons/NgolIcon'
import { NodeResizeHandles } from './NodeResizeHandle'
import './AnnotationNode.css'

const ANNOTATION_MIN_WIDTH = 120
const ANNOTATION_MIN_HEIGHT = 60

export interface AnnotationNodeData extends Record<string, unknown> {
  annotationId: string
  text: string
  color?: string
  width: number
  height: number
  onTextChange: (id: string, text: string) => void
  onResize: (id: string, width: number, height: number) => void
  onDelete: (id: string) => void
}

export const AnnotationNode = memo(function AnnotationNode({ id, data, selected }: { id: string; data: AnnotationNodeData; selected?: boolean }) {
  const {
    annotationId,
    text,
    color = '#fffde7',
    width = 200,
    height = 100,
    onTextChange,
    onDelete,
    onResize,
  } = data

  const handleResize = useCallback((w: number, h: number) => {
    onResize(annotationId, w, h)
  }, [annotationId, onResize])

  const [isEditing, setIsEditing] = useState(false)
  const [editValue, setEditValue] = useState(text)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const commitEdit = useCallback(() => {
    const trimmed = editValue.trim()
    onTextChange(annotationId, trimmed)
    setIsEditing(false)
  }, [annotationId, editValue, onTextChange])

  useEffect(() => {
    if (isEditing && textareaRef.current) {
      textareaRef.current.focus()
      textareaRef.current.select()
    }
  }, [isEditing])

  const handleDoubleClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    e.nativeEvent.stopPropagation()
    e.nativeEvent.stopImmediatePropagation()
    e.preventDefault()
    setEditValue(text)
    setIsEditing(true)
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setIsEditing(false)
    } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      commitEdit()
    }
    e.stopPropagation()
  }

  return (
    <div
      className={`annotation-node${selected ? ' selected' : ''}`}
      style={{
        width,
        minHeight: height,
        background: color,
        '--annotation-color': color,
      } as React.CSSProperties}
      onDoubleClick={handleDoubleClick}
    >
      {/* 選択時: 角ドラッグでリサイズ */}
      {selected && (
        <NodeResizeHandles
          nodeId={id}
          targetSelector=".annotation-node"
          minWidth={ANNOTATION_MIN_WIDTH}
          minHeight={ANNOTATION_MIN_HEIGHT}
          onResize={handleResize}
        />
      )}
      {/* 削除ボタン */}
      {selected && (
        <button
          className="annotation-delete-btn"
          title="Delete annotation"
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
          onClick={e => { e.stopPropagation(); onDelete(annotationId) }}
        >
          <NgolIcon name="close" size={11} />
        </button>
      )}

      {isEditing ? (
        <textarea
          ref={textareaRef}
          className="annotation-textarea"
          value={editValue}
          onChange={e => setEditValue(e.target.value)}
          onBlur={commitEdit}
          onKeyDown={handleKeyDown}
          onMouseDown={e => e.stopPropagation()}
          onPointerDown={e => e.stopPropagation()}
          style={{ width: '100%', minHeight: height - 16, resize: 'none' }}
        />
      ) : (
        <div className="annotation-text">
          {text || <span className="annotation-placeholder">Double-click to edit...</span>}
        </div>
      )}
    </div>
  )
})
