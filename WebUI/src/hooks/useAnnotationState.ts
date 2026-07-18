import { useState, useCallback, useRef } from 'react'
import type { Node } from '@xyflow/react'
import type { NodeAnnotation } from '../types/protocol'

export interface UseAnnotationStateResult {
  annotations: NodeAnnotation[]
  addAnnotation: (position: { x: number; y: number }) => void
  updateAnnotationText: (id: string, text: string) => void
  deleteAnnotation: (id: string) => void
  resetAnnotations: (initial: NodeAnnotation[]) => void
}

export function useAnnotationState(): UseAnnotationStateResult {
  const [annotations, setAnnotations] = useState<NodeAnnotation[]>([])
  const annotationsRef = useRef<NodeAnnotation[]>([])

  const setAndSync = useCallback((next: NodeAnnotation[]) => {
    annotationsRef.current = next
    setAnnotations(next)
  }, [])

  const addAnnotation = useCallback((position: { x: number; y: number }) => {
    const newAnnotation: NodeAnnotation = {
      id: `annot-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      text: '',
      position,
      width: 200,
      height: 100,
      color: '#fffde7',
    }
    setAndSync([...annotationsRef.current, newAnnotation])
  }, [setAndSync])

  const updateAnnotationText = useCallback((id: string, text: string) => {
    setAndSync(annotationsRef.current.map(a => a.id === id ? { ...a, text } : a))
  }, [setAndSync])

  const deleteAnnotation = useCallback((id: string) => {
    setAndSync(annotationsRef.current.filter(a => a.id !== id))
  }, [setAndSync])

  const resetAnnotations = useCallback((initial: NodeAnnotation[]) => {
    setAndSync([...initial])
  }, [setAndSync])

  return { annotations, addAnnotation, updateAnnotationText, deleteAnnotation, resetAnnotations }
}

/** Annotation rfNode callbacks wired to ReactFlow node state */
export function useAnnotationRfCallbacks(setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>) {
  const stableAnnotationTextChange = useCallback((annotId: string, text: string) => {
    setRfNodes(prev => prev.map(n =>
      n.id === `annot-${annotId}` ? { ...n, data: { ...n.data, text } } : n
    ))
  }, [setRfNodes])

  const stableAnnotationDelete = useCallback((annotId: string) => {
    setRfNodes(prev => prev.filter(n => n.id !== `annot-${annotId}`))
  }, [setRfNodes])

  /** 角ドラッグリサイズ。position はハンドル側が直接 setNodes で更新するため、ここでは width/height のみ反映する。 */
  const stableAnnotationResize = useCallback((annotId: string, width: number, height: number) => {
    setRfNodes(prev => prev.map(n =>
      n.id === `annot-${annotId}` ? { ...n, data: { ...n.data, width, height } } : n
    ))
  }, [setRfNodes])

  const addAnnotationAtPosition = useCallback((position: { x: number; y: number }) => {
    const annotId = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
    setRfNodes(prev => [...prev, {
      id: `annot-${annotId}`,
      type: 'annotation',
      position,
      data: {
        annotationId: annotId,
        text: '',
        color: '#fffde7',
        width: 200,
        height: 100,
        onTextChange: () => {},
        onDelete: () => {},
        onResize: () => {},
      } as unknown as Record<string, unknown>,
      draggable: true,
      selectable: true,
    }])
  }, [setRfNodes])

  return { stableAnnotationTextChange, stableAnnotationDelete, stableAnnotationResize, addAnnotationAtPosition }
}
