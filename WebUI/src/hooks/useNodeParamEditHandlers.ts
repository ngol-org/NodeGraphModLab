import { useCallback, useRef } from 'react'
import type { Node, ReactFlowInstance } from '@xyflow/react'

export interface UseNodeParamEditHandlersParams {
  rfRef: React.MutableRefObject<ReactFlowInstance | null>
  selectedNodeId: string | null
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  pushHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['push']
  recordHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['record']
}

export function useNodeParamEditHandlers({
  rfRef,
  selectedNodeId,
  setRfNodes,
  pushHistory,
  recordHistory,
}: UseNodeParamEditHandlersParams) {
  const paramEditDragging = useRef(false)
  const paramEditNodeId = useRef<string | null>(null)
  const paramEditInitialValues = useRef<Record<string, unknown> | null>(null)

  const handleParamEditStart = useCallback(() => {
    if (!selectedNodeId) return
    const node = rfRef.current?.getNodes().find(n => n.id === selectedNodeId)
    if (!node) return
    paramEditNodeId.current = selectedNodeId
    paramEditInitialValues.current = {
      ...(node.data as { paramValues: Record<string, unknown> }).paramValues
    }
    paramEditDragging.current = true
  }, [selectedNodeId, rfRef])

  const handleParamEditEnd = useCallback(() => {
    paramEditDragging.current = false
    const nodeId = paramEditNodeId.current
    if (!nodeId || !paramEditInitialValues.current) return
    const node = rfRef.current?.getNodes().find(n => n.id === nodeId)
    if (!node) return
    const initialVals = paramEditInitialValues.current
    paramEditInitialValues.current = null
    paramEditNodeId.current = null
    const currentParams = (node.data as { paramValues: Record<string, unknown> }).paramValues
    if (!Object.keys(initialVals).some(k => currentParams[k] !== initialVals[k])) return
    const finalVals = { ...currentParams }
    recordHistory({
      label: 'Update Property',
      do: () => setRfNodes(prev => prev.map(n =>
        n.id === nodeId
          ? { ...n, data: { ...n.data, paramValues: {
              ...(n.data as { paramValues: Record<string, unknown> }).paramValues,
              ...finalVals
            }}}
          : n
      )),
      undo: () => setRfNodes(prev => prev.map(n =>
        n.id === nodeId
          ? { ...n, data: { ...n.data, paramValues: {
              ...(n.data as { paramValues: Record<string, unknown> }).paramValues,
              ...initialVals
            }}}
          : n
      )),
    })
  }, [setRfNodes, recordHistory, rfRef])

  const handleParamChange = useCallback((paramName: string, value: unknown) => {
    if (!selectedNodeId) return
    const nodeId = selectedNodeId
    if (paramEditDragging.current) {
      setRfNodes(prev => prev.map(n =>
        n.id === nodeId
          ? { ...n, data: { ...n.data, paramValues: {
              ...(n.data as { paramValues: Record<string, unknown> }).paramValues,
              [paramName]: value
            }}}
          : n
      ))
      return
    }
    const node = rfRef.current?.getNodes().find(n => n.id === selectedNodeId)
    if (!node) return
    const prevParamValue = (node.data as { paramValues: Record<string, unknown> }).paramValues[paramName]
    pushHistory({
      label: 'Update Property',
      do: () => setRfNodes(prev => prev.map(n =>
        n.id === nodeId
          ? { ...n, data: { ...n.data, paramValues: {
              ...(n.data as { paramValues: Record<string, unknown> }).paramValues,
              [paramName]: value
            }}}
          : n
      )),
      undo: () => setRfNodes(prev => prev.map(n =>
        n.id === nodeId
          ? { ...n, data: { ...n.data, paramValues: {
              ...(n.data as { paramValues: Record<string, unknown> }).paramValues,
              [paramName]: prevParamValue
            }}}
          : n
      )),
    })
  }, [selectedNodeId, setRfNodes, pushHistory, rfRef])

  return {
    handleParamEditStart,
    handleParamEditEnd,
    handleParamChange,
  }
}
