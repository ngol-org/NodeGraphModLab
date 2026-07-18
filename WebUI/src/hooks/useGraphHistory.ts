import { useState, useRef, useCallback } from 'react'

export interface GraphCommand {
  label: string
  do(): void
  undo(): void
}

const MAX_HISTORY = 100

export function useGraphHistory() {
  const undoStack = useRef<GraphCommand[]>([])
  const redoStack = useRef<GraphCommand[]>([])
  const [canUndo, setCanUndo] = useState(false)
  const [canRedo, setCanRedo] = useState(false)

  const push = useCallback((cmd: GraphCommand) => {
    cmd.do()
    undoStack.current.push(cmd)
    if (undoStack.current.length > MAX_HISTORY) undoStack.current.shift()
    redoStack.current = []
    setCanUndo(true)
    setCanRedo(false)
  }, [])

  const undo = useCallback(() => {
    const cmd = undoStack.current.pop()
    if (!cmd) return
    cmd.undo()
    redoStack.current.push(cmd)
    setCanUndo(undoStack.current.length > 0)
    setCanRedo(true)
  }, [])

  const redo = useCallback(() => {
    const cmd = redoStack.current.pop()
    if (!cmd) return
    cmd.do()
    undoStack.current.push(cmd)
    setCanUndo(true)
    setCanRedo(redoStack.current.length > 0)
  }, [])

  const clear = useCallback(() => {
    undoStack.current = []
    redoStack.current = []
    setCanUndo(false)
    setCanRedo(false)
  }, [])

  // do() を呼ばずにスタックに積む（ドラッグ終了時のバッチコミット用）
  const record = useCallback((cmd: GraphCommand) => {
    undoStack.current.push(cmd)
    if (undoStack.current.length > MAX_HISTORY) undoStack.current.shift()
    redoStack.current = []
    setCanUndo(true)
    setCanRedo(false)
  }, [])

  return { push, undo, redo, clear, record, canUndo, canRedo }
}
