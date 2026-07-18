import { describe, expect, it } from 'vitest'
import { buildAutoConnectEdge, getFirstInputPortName, getFirstOutputPortName, EXEC_IN_PORT_NAME, EXEC_OUT_PORT_NAME } from '../lib/dragAddNode'
import type { NodeTypeInfo } from '../types/protocol'

const NODE_TYPE_WITH_INPUTS: NodeTypeInfo = {
  id: 'ngol.logic.sample',
  category: 'Logic',
  displayName: 'Sample',
  description: 'sample',
  ports: [
    { name: 'result', direction: 'output', dataType: 'number', isRequired: false },
    { name: 'value', direction: 'input', dataType: 'number', isRequired: true },
    { name: 'fallback', direction: 'input', dataType: 'number', isRequired: false },
  ],
}

describe('dragAddNode helpers', () => {
  it('returns first input port name', () => {
    expect(getFirstInputPortName(NODE_TYPE_WITH_INPUTS)).toBe('value')
  })

  it('returns null when node has no input ports', () => {
    expect(getFirstInputPortName({
      ...NODE_TYPE_WITH_INPUTS,
      ports: NODE_TYPE_WITH_INPUTS.ports.filter(port => port.direction !== 'input'),
    })).toBeNull()
  })

  it('returns first output port name', () => {
    expect(getFirstOutputPortName(NODE_TYPE_WITH_INPUTS)).toBe('result')
  })

  it('returns null when node has no output ports', () => {
    expect(getFirstOutputPortName({
      ...NODE_TYPE_WITH_INPUTS,
      ports: NODE_TYPE_WITH_INPUTS.ports.filter(port => port.direction !== 'output'),
    })).toBeNull()
  })

  it('builds auto-connect edge from a dragged output port to the first input port', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: 'result', handleType: 'source' },
      'node-b',
      NODE_TYPE_WITH_INPUTS,
    )

    expect(edge).toMatchObject({
      source: 'node-a',
      sourceHandle: 'result',
      target: 'node-b',
      targetHandle: 'value',
      animated: true,
    })
  })

  it('builds auto-connect edge from the first output port to a dragged input port', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: 'value', handleType: 'target' },
      'node-b',
      NODE_TYPE_WITH_INPUTS,
    )

    expect(edge).toMatchObject({
      source: 'node-b',
      sourceHandle: 'result',
      target: 'node-a',
      targetHandle: 'value',
      animated: true,
    })
  })

  it('returns null when pending connection is missing', () => {
    expect(buildAutoConnectEdge(null, 'node-b', NODE_TYPE_WITH_INPUTS)).toBeNull()
  })

  it('falls back to the synthetic exec-in port when target node has no input ports', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: 'result', handleType: 'source' },
      'node-b',
      {
        ...NODE_TYPE_WITH_INPUTS,
        ports: NODE_TYPE_WITH_INPUTS.ports.filter(port => port.direction !== 'input'),
      },
    )

    expect(edge).toMatchObject({
      source: 'node-a',
      sourceHandle: 'result',
      target: 'node-b',
      targetHandle: EXEC_IN_PORT_NAME,
    })
  })

  it('falls back to the synthetic exec-out port when new node has no output ports (dragging from an input handle)', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: 'value', handleType: 'target' },
      'node-b',
      {
        ...NODE_TYPE_WITH_INPUTS,
        ports: NODE_TYPE_WITH_INPUTS.ports.filter(port => port.direction !== 'output'),
      },
    )

    expect(edge).toMatchObject({
      source: 'node-b',
      sourceHandle: EXEC_OUT_PORT_NAME,
      target: 'node-a',
      targetHandle: 'value',
    })
  })

  it('forces exec-in connection when dragging from the exec-out handle, even if the new node has real input ports', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: EXEC_OUT_PORT_NAME, handleType: 'source' },
      'node-b',
      NODE_TYPE_WITH_INPUTS,
    )

    expect(edge).toMatchObject({
      source: 'node-a',
      sourceHandle: EXEC_OUT_PORT_NAME,
      target: 'node-b',
      targetHandle: EXEC_IN_PORT_NAME,
    })
  })

  it('forces exec-out connection when dragging from the exec-in handle, even if the new node has real output ports', () => {
    const edge = buildAutoConnectEdge(
      { nodeId: 'node-a', handleId: EXEC_IN_PORT_NAME, handleType: 'target' },
      'node-b',
      NODE_TYPE_WITH_INPUTS,
    )

    expect(edge).toMatchObject({
      source: 'node-b',
      sourceHandle: EXEC_OUT_PORT_NAME,
      target: 'node-a',
      targetHandle: EXEC_IN_PORT_NAME,
    })
  })
})
