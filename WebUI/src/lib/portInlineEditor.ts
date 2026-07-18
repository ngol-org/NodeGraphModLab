import type { NodePortInfo } from '../types/protocol'

/** Data types CustomNode can render as an inline in-node editor. */
export function portSupportsInlineEditor(dataType: string): boolean {
  const dt = dataType.toLowerCase()
  if (dt === 'boolean' || dt === 'bool') return true
  if (dt === 'number' || dt === 'float' || dt === 'double' || dt === 'int' || dt === 'integer') return true
  if (dt === 'string') return true
  if (dt.startsWith('enum:')) return true
  if (dt === 'color') return true
  return false
}

/**
 * opt-in: only ports with showInlineEditor === true get an in-node editor.
 * Output ports additionally require the node to have no input ports (avoids
 * clutter on nodes that mix constant output with wired inputs).
 */
export function shouldShowPortInlineEditor(
  port: Pick<NodePortInfo, 'dataType' | 'showInlineEditor' | 'direction'>,
  inputPortCount: number,
): boolean {
  if (port.showInlineEditor !== true) return false
  if (!portSupportsInlineEditor(port.dataType)) return false
  if (port.direction === 'output' && inputPortCount > 0) return false
  return true
}
