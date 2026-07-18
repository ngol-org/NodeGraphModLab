import { useCallback, useRef } from 'react'
import { useReactFlow } from '@xyflow/react'

export type ResizeCorner = 'nw' | 'ne' | 'sw' | 'se'

interface DragState {
  startClientX: number
  startClientY: number
  startWidth: number
  startHeight: number
  startPosX: number
  startPosY: number
}

interface NodeResizeHandleProps {
  /** ReactFlow ノードID（position 更新に使用） */
  nodeId: string
  corner: ResizeCorner
  minWidth: number
  minHeight: number
  /** リサイズ対象要素を特定する CSS セレクタ（例: '.custom-node'）。ドラッグ開始時に実測サイズを基準にする */
  targetSelector: string
  /** ドラッグ中に継続的に呼ばれる。呼び出し側で data への反映方法を決める（data.size / data.width,height 等） */
  onResize: (width: number, height: number) => void
}

/**
 * ノード角の1箇所に配置するリサイズハンドル。
 * ドラッグ開始時に対象要素の実測サイズ（getBoundingClientRect）を基準にすることで、
 * 自動サイズ（内容依存の height 等）からの初回リサイズでも値が飛ばない。
 * nw/ne/sw コーナーは反対側の角を固定するため、サイズ変更と同時に
 * ReactFlow の node.position も調整する（position はこのコンポーネントが直接更新し、
 * width/height の保存先は呼び出し側に委ねる）。
 */
export function NodeResizeHandle({ nodeId, corner, minWidth, minHeight, targetSelector, onResize }: NodeResizeHandleProps) {
  const { setNodes, getNode, getZoom } = useReactFlow()
  const dragRef = useRef<DragState | null>(null)

  const handlePointerDown = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    e.stopPropagation()
    e.preventDefault()
    const node = getNode(nodeId)
    const target = e.currentTarget.closest(targetSelector)
    const rect = target?.getBoundingClientRect()
    const zoom = getZoom() || 1
    // getBoundingClientRect はスクリーン座標（ズーム後）なので、フロー座標系に戻す
    dragRef.current = {
      startClientX: e.clientX,
      startClientY: e.clientY,
      startWidth: rect ? rect.width / zoom : minWidth,
      startHeight: rect ? rect.height / zoom : minHeight,
      startPosX: node?.position.x ?? 0,
      startPosY: node?.position.y ?? 0,
    }
    e.currentTarget.setPointerCapture(e.pointerId)
  }, [nodeId, minWidth, minHeight, targetSelector, getNode, getZoom])

  const handlePointerMove = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current
    if (!drag) return
    const zoom = getZoom() || 1
    // ポインタ移動量もスクリーン座標なのでズームで割ってフロー座標系の移動量に変換
    const dx = (e.clientX - drag.startClientX) / zoom
    const dy = (e.clientY - drag.startClientY) / zoom
    const growX = corner === 'ne' || corner === 'se' ? dx : -dx
    const growY = corner === 'sw' || corner === 'se' ? dy : -dy
    const newWidth = Math.max(minWidth, drag.startWidth + growX)
    const newHeight = Math.max(minHeight, drag.startHeight + growY)

    // west/north 側の角は反対側の角を固定するよう position をずらす
    const deltaW = newWidth - drag.startWidth
    const deltaH = newHeight - drag.startHeight
    const newPosX = corner === 'nw' || corner === 'sw' ? drag.startPosX - deltaW : drag.startPosX
    const newPosY = corner === 'nw' || corner === 'ne' ? drag.startPosY - deltaH : drag.startPosY

    if (newPosX !== drag.startPosX || newPosY !== drag.startPosY) {
      setNodes(nds => nds.map(n => n.id === nodeId ? { ...n, position: { x: newPosX, y: newPosY } } : n))
    }
    onResize(newWidth, newHeight)
  }, [corner, minWidth, minHeight, nodeId, setNodes, onResize, getZoom])

  const handlePointerUp = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return
    dragRef.current = null
    e.currentTarget.releasePointerCapture(e.pointerId)
  }, [])

  return (
    <div
      className={`node-resize-handle node-resize-handle-${corner} nodrag nopan`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      title="Drag to resize"
    />
  )
}

/** 4角のリサイズハンドルをまとめて配置するヘルパー。 */
export function NodeResizeHandles(props: Omit<NodeResizeHandleProps, 'corner'>) {
  const corners: ResizeCorner[] = ['nw', 'ne', 'sw', 'se']
  return (
    <>
      {corners.map(corner => (
        <NodeResizeHandle key={corner} {...props} corner={corner} />
      ))}
    </>
  )
}
