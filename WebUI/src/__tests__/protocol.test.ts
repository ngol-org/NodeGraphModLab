/**
 * プロトコル型の基本テスト
 * - NodeTypeInfo / NodePortInfo / NodeGraphData などが期待通りのShape
 */
import { describe, it, expect } from 'vitest'
import type {
  NodeTypeInfo,
  NodePortInfo,
  NodeGraphData,
  NodeInstance,
  NodeConnection,
  ExecutionLogPush,
  GraphSummary,
  WelcomeMessage,
  PersistentNodeChangedMessage,
  PersistentNodeInfo,
  ExportNodesResponse,
} from '../types/protocol'

describe('NodePortInfo', () => {
  it('inputポートを作成できる', () => {
    const port: NodePortInfo = {
      name: 'value',
      direction: 'input',
      dataType: 'number',
      isRequired: true,
    }
    expect(port.direction).toBe('input')
    expect(port.isRequired).toBe(true)
  })

  it('outputポートを作成できる', () => {
    const port: NodePortInfo = {
      name: 'result',
      direction: 'output',
      dataType: 'number',
      isRequired: false,
    }
    expect(port.direction).toBe('output')
  })
})

describe('NodeTypeInfo', () => {
  it('ノードタイプを作成できる', () => {
    const node: NodeTypeInfo = {
      id: 'ngol.logic.add',
      category: 'Logic',
      displayName: '加算',
      nodeVersion: '1.1.0',
      ports: [
        { name: 'a', direction: 'input', dataType: 'number', isRequired: true },
        { name: 'b', direction: 'input', dataType: 'number', isRequired: true },
        { name: 'result', direction: 'output', dataType: 'number', isRequired: false },
      ],
    }
    expect(node.id).toBe('ngol.logic.add')
    expect(node.nodeVersion).toBe('1.1.0')
    expect(node.ports).toHaveLength(3)
    expect(node.ports.filter(p => p.direction === 'input')).toHaveLength(2)
    expect(node.ports.filter(p => p.direction === 'output')).toHaveLength(1)
  })
})

describe('NodeGraphData', () => {
  it('空のグラフデータを作成できる', () => {
    const graph: NodeGraphData = {
      id: 'test-id-001',
      name: 'テストグラフ',
      description: '',
      version: 1,
      createdAt: new Date().toISOString(),
      nodes: [],
      connections: [],
      fragments: [],
      fragmentLinks: [],
    }
    expect(graph.nodes).toHaveLength(0)
    expect(graph.connections).toHaveLength(0)
    expect(graph.version).toBe(1)
  })

  it('ノードと接続を含むグラフデータを作成できる', () => {
    const node1: NodeInstance = {
      instanceId: 'node-a',
      nodeTypeId: 'ngol.logic.const_number',
      nodeTypeVersion: '1.0.0',
      position: { x: 100, y: 100 },
      paramValues: { value: 42 },
    }
    const node2: NodeInstance = {
      instanceId: 'node-b',
      nodeTypeId: 'ngol.logic.log',
      position: { x: 300, y: 100 },
      paramValues: {},
    }
    const conn: NodeConnection = {
      fromNodeInstanceId: 'node-a',
      fromPortName: 'value',
      toNodeInstanceId: 'node-b',
      toPortName: 'value',
    }
    const graph: NodeGraphData = {
      id: 'test-id-002',
      name: '接続テスト',
      description: '',
      version: 1,
      createdAt: new Date().toISOString(),
      nodes: [node1, node2],
      connections: [conn],
      fragments: [],
      fragmentLinks: [],
    }
    expect(graph.nodes).toHaveLength(2)
    expect(graph.nodes[0].nodeTypeVersion).toBe('1.0.0')
    expect(graph.connections).toHaveLength(1)
    expect(graph.connections[0].fromNodeInstanceId).toBe('node-a')
  })
})

describe('ExecutionLogPush', () => {
  it('ログメッセージを作成できる', () => {
    const log: ExecutionLogPush = {
      type: 'execution_log',
      message: '実行完了',
      level: 'info',
      timestampMs: Date.now(),
    }
    expect(log.type).toBe('execution_log')
    expect(log.level).toBe('info')
  })

  it('全ログレベルが型安全である', () => {
    const levels: ExecutionLogPush['level'][] = ['debug', 'info', 'warning', 'error']
    expect(levels).toHaveLength(4)
  })
})

describe('GraphSummary', () => {
  it('グラフサマリーを作成できる', () => {
    const summary: GraphSummary = {
      id: 'graph-001',
      name: 'My Graph',
    }
    expect(summary.id).toBe('graph-001')
    expect(summary.name).toBe('My Graph')
  })
})

describe('WelcomeMessage', () => {
  it('welcome メッセージを作成できる', () => {
    const msg: WelcomeMessage = {
      type: 'welcome',
      pluginVersion: 'v0.6.0',
      gameName: 'SampleHostApp',
      runtimeType: 'IL2CPP',
    }
    expect(msg.type).toBe('welcome')
    expect(msg.pluginVersion).toBe('v0.6.0')
    expect(msg.gameName).toBe('SampleHostApp')
    expect(msg.runtimeType).toBe('IL2CPP')
  })
})

describe('PersistentNodeChangedMessage', () => {
  it('空の activeNodes リストで作成できる', () => {
    const msg: PersistentNodeChangedMessage = {
      type: 'persistent_node_changed',
      activeNodes: [],
    }
    expect(msg.type).toBe('persistent_node_changed')
    expect(msg.activeNodes).toHaveLength(0)
  })

  it('activeNodes に PersistentNodeInfo を含めて作成できる', () => {
    const info: PersistentNodeInfo = {
      nodeInstanceId: 'uuid-1234',
      displayName: 'Inspector',
      graphName: 'scene-graph',
    }
    const msg: PersistentNodeChangedMessage = {
      type: 'persistent_node_changed',
      activeNodes: [info],
    }
    expect(msg.activeNodes).toHaveLength(1)
    expect(msg.activeNodes[0].nodeInstanceId).toBe('uuid-1234')
    expect(msg.activeNodes[0].displayName).toBe('Inspector')
  })
})

describe('ExportNodesResponse', () => {
  it('成功レスポンスを作成できる', () => {
    const msg: ExportNodesResponse = {
      type: 'export_nodes_response',
      success: true,
      savedPath: 'Nodes/CustomNodes/dll/MyNodePack.dll',
    }
    expect(msg.success).toBe(true)
    expect(msg.savedPath).toBe('Nodes/CustomNodes/dll/MyNodePack.dll')
  })

  it('失敗レスポンスを作成できる', () => {
    const msg: ExportNodesResponse = {
      type: 'export_nodes_response',
      success: false,
      errorMessage: 'assemblyName is required.',
    }
    expect(msg.success).toBe(false)
    expect(msg.errorMessage).toBe('assemblyName is required.')
  })
})

