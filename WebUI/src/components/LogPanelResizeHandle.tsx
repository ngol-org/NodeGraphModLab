import { useCallback, useRef } from 'react'

interface DragState {
  startClientY: number
  startHeight: number
  maxHeight: number
}

interface LogPanelResizeHandleProps {
  height: number
  onResize: (height: number, maxHeight: number) => void
}

/**
 * ActiveLog（ExecutionLogPanel）とキャンバスの境界に配置する縦方向リサイズハンドル。
 * ドラッグ開始時に window.innerHeight から上限を1回だけ算出し、ドラッグ中は固定する。
 */
export function LogPanelResizeHandle({ height, onResize }: LogPanelResizeHandleProps) {
  const dragRef = useRef<DragState | null>(null)

  const handlePointerDown = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault()
    dragRef.current = {
      startClientY: e.clientY,
      startHeight: height,
      // 上部固定領域（menubar+header+キャンバス最低確保領域）を差し引いた上限
      maxHeight: window.innerHeight - 28 - 44 - 200,
    }
    e.currentTarget.setPointerCapture(e.pointerId)
  }, [height])

  const handlePointerMove = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current
    if (!drag) return
    const dy = drag.startClientY - e.clientY
    onResize(drag.startHeight + dy, drag.maxHeight)
  }, [onResize])

  const handlePointerUp = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return
    dragRef.current = null
    e.currentTarget.releasePointerCapture(e.pointerId)
  }, [])

  return (
    <div
      className="log-resize-handle"
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      title="Drag to resize log panel"
    />
  )
}
