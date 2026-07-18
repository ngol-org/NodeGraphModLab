import { useState, useRef, useCallback, useEffect } from 'react'
import type { ReactFlowInstance, Node } from '@xyflow/react'

type MenuPos = { x: number; y: number; canvasX: number; canvasY: number }
type NodeContextMenuState = { x: number; y: number; nodeId: string }

export function useMenuState(rfRef: React.RefObject<ReactFlowInstance | null>) {
  const [addMenuPos, setAddMenuPos] = useState<MenuPos | null>(null)
  const [nodeContextMenu, setNodeContextMenu] = useState<NodeContextMenuState | null>(null)
  const [paneContextMenuPos, setPaneContextMenuPos] = useState<MenuPos | null>(null)
  const [paneClearSubmenuOpen, setPaneClearSubmenuOpen] = useState(false)
  const [fragmentImportMenuPos, setFragmentImportMenuPos] = useState<MenuPos | null>(null)
  const lastCanvasClickRef = useRef<{ x: number; y: number }>({ x: 200, y: 200 })

  const handleNodeContextMenu = useCallback((e: React.MouseEvent, node: Node) => {
    e.preventDefault()
    e.stopPropagation()
    setAddMenuPos(null)
    setNodeContextMenu({ x: e.clientX, y: e.clientY, nodeId: node.id })
  }, [])

  const handlePaneContextMenu = useCallback((e: MouseEvent | React.MouseEvent) => {
    e.preventDefault()
    const flowPos = rfRef.current?.screenToFlowPosition({ x: e.clientX, y: e.clientY })
    const canvasX = flowPos?.x ?? e.clientX
    const canvasY = flowPos?.y ?? e.clientY
    lastCanvasClickRef.current = { x: canvasX, y: canvasY }
    setAddMenuPos(null)
    setNodeContextMenu(null)
    setFragmentImportMenuPos(null)
    setPaneClearSubmenuOpen(false)
    setPaneContextMenuPos({ x: e.clientX, y: e.clientY, canvasX, canvasY })
  }, [rfRef])

  const closeAllMenus = useCallback(() => {
    setAddMenuPos(null)
    setNodeContextMenu(null)
    setPaneContextMenuPos(null)
    setFragmentImportMenuPos(null)
    setPaneClearSubmenuOpen(false)
  }, [])

  useEffect(() => {
    if (!paneContextMenuPos) return
    const handler = () => { setPaneContextMenuPos(null); setPaneClearSubmenuOpen(false) }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [paneContextMenuPos])

  return {
    addMenuPos,
    setAddMenuPos,
    nodeContextMenu,
    setNodeContextMenu,
    paneContextMenuPos,
    setPaneContextMenuPos,
    paneClearSubmenuOpen,
    setPaneClearSubmenuOpen,
    fragmentImportMenuPos,
    setFragmentImportMenuPos,
    lastCanvasClickRef,
    handleNodeContextMenu,
    handlePaneContextMenu,
    closeAllMenus,
  }
}
