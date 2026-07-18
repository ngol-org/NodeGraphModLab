import { describe, it, expect } from 'vitest'
import { autoPortLayout, PORT_BASE, PORT_STEP, EXEC_TOP } from '../lib/nodePortLayout'
import type { NodeTypeInfo, NodePortInfo } from '../types/protocol'

function makePort(name: string, direction: 'input' | 'output'): NodePortInfo {
  return { name, direction, dataType: 'number', isRequired: false }
}

function makeNodeTypeInfo(ports: NodePortInfo[]): NodeTypeInfo {
  return { id: 'test.node', category: 'Test', displayName: 'Test Node', ports }
}

describe('autoPortLayout', () => {
  it('入力・出力ポートを PORT_BASE 起点・PORT_STEP 間隔で並べる', () => {
    const info = makeNodeTypeInfo([
      makePort('a', 'input'),
      makePort('b', 'input'),
      makePort('result', 'output'),
    ])

    const layout = autoPortLayout(info)

    expect(layout.inputs).toEqual({ a: PORT_BASE, b: PORT_BASE + PORT_STEP })
    expect(layout.outputs).toEqual({ result: PORT_BASE })
  })

  it('ポートが無い方向は空オブジェクトを返す', () => {
    const info = makeNodeTypeInfo([makePort('value', 'output')])

    const layout = autoPortLayout(info)

    expect(layout.inputs).toEqual({})
    expect(layout.outputs).toEqual({ value: PORT_BASE })
  })

  it('PORT_BASE は exec 専用の EXEC_TOP と衝突しない', () => {
    expect(PORT_BASE).not.toBe(EXEC_TOP)
  })
})
