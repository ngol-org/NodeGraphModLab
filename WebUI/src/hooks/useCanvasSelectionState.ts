import { useState, useEffect, useRef } from 'react'
import { emitPluginEvent } from '../webuiPlugin/pluginEventBus'

export function useCanvasSelectionState(selectedNodeId: string | null) {
  const lastMouseRef = useRef<{ x: number; y: number }>({ x: 0, y: 0 })
  // 複数選択: 修飾キー追跡（Ctrl=追加, Shift=除外）
  const modifierKeyRef = useRef<'ctrl' | 'shift' | null>(null)
  // 複数選択: ドラッグ選択開始時の選択済みノードID
  const preSelectionRef = useRef<Set<string>>(new Set())
  // ドラッグ開始前の最後の確定選択状態（Ctrl+drag で既存選択を保持するため）
  const lastKnownSelectionRef = useRef<Set<string>>(new Set())
  // Space キー押下中 → ノードを draggable/selectable=false にしてキャンバスパンを優先
  const [isSpacePressed, setIsSpacePressed] = useState(false)
  // ボックス選択中（Ctrl/Shift ドラッグ時に既存選択を視覚的に保持するため）
  const [isDragSelecting, setIsDragSelecting] = useState(false)
  // 複数選択IDの信頼できるソース（onSelectionEnd/onNodeClick/onPaneClick で直接管理）
  const [multiSelectedIds, setMultiSelectedIds] = useState<Set<string>>(new Set())

  // プラグイン拡張: ノード選択変更イベントを発火
  useEffect(() => {
    const ids = multiSelectedIds.size > 0
      ? Array.from(multiSelectedIds)
      : selectedNodeId ? [selectedNodeId] : []
    emitPluginEvent('selection_changed', { selectedNodeIds: ids })
  }, [selectedNodeId, multiSelectedIds])

  // 修飾キー（Ctrl=追加選択 / Shift=除外選択）の状態を追跡
  // Space キー押下中はノードを draggable=false にしてキャンバスパン優先
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === ' ') setIsSpacePressed(true)
      if (e.ctrlKey || e.metaKey) modifierKeyRef.current = 'ctrl'
      else if (e.shiftKey) modifierKeyRef.current = 'shift'
    }
    const onKeyUp = (e: KeyboardEvent) => {
      if (e.key === ' ') setIsSpacePressed(false)
      if (!e.ctrlKey && !e.metaKey && !e.shiftKey) modifierKeyRef.current = null
      else if (e.shiftKey) modifierKeyRef.current = 'shift'
      else if (e.ctrlKey || e.metaKey) modifierKeyRef.current = 'ctrl'
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
    }
  }, [])

  // マウス位置を追跡
  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      lastMouseRef.current = { x: e.clientX, y: e.clientY }
    }
    window.addEventListener('mousemove', handleMouseMove)
    return () => window.removeEventListener('mousemove', handleMouseMove)
  }, [])

  return {
    isSpacePressed,
    isDragSelecting,
    setIsDragSelecting,
    multiSelectedIds,
    setMultiSelectedIds,
    modifierKeyRef,
    preSelectionRef,
    lastKnownSelectionRef,
    lastMouseRef,
  }
}
