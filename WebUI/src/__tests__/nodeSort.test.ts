import { describe, it, expect } from 'vitest'
import { groupAndSortNodes } from '../lib/nodeSort'
import type { NodeTypeInfo } from '../types/protocol'

function makeNode(id: string, category: string, displayName: string, lastModified?: string): NodeTypeInfo {
  return { id, category, displayName, ports: [], lastModified }
}

describe('groupAndSortNodes', () => {
  const nodes = [
    makeNode('b1', 'Beta', 'Banana'),
    makeNode('a2', 'Alpha', 'Zebra'),
    makeNode('a1', 'Alpha', 'Apple'),
    makeNode('b2', 'Beta', 'Avocado'),
  ]

  it('category モードはカテゴリ名をアルファベット順に並べ、カテゴリ内は入力順を維持する', () => {
    const groups = groupAndSortNodes(nodes, 'category')
    expect(groups.map(g => g.category)).toEqual(['Alpha', 'Beta'])
    expect(groups[0].nodes.map(n => n.id)).toEqual(['a2', 'a1'])
    expect(groups[1].nodes.map(n => n.id)).toEqual(['b1', 'b2'])
  })

  it('name-asc モードはカテゴリ分けせず displayName 昇順のフラット1グループにする', () => {
    const groups = groupAndSortNodes(nodes, 'name-asc')
    expect(groups).toHaveLength(1)
    expect(groups[0].category).toBeNull()
    expect(groups[0].nodes.map(n => n.displayName)).toEqual(['Apple', 'Avocado', 'Banana', 'Zebra'])
  })

  it('name-desc モードはカテゴリ分けせず displayName 降順のフラット1グループにする', () => {
    const groups = groupAndSortNodes(nodes, 'name-desc')
    expect(groups).toHaveLength(1)
    expect(groups[0].category).toBeNull()
    expect(groups[0].nodes.map(n => n.displayName)).toEqual(['Zebra', 'Banana', 'Avocado', 'Apple'])
  })

  it('空配列を渡しても例外を起こさない', () => {
    expect(groupAndSortNodes([], 'category')).toEqual([])
    expect(groupAndSortNodes([], 'name-asc')).toEqual([{ category: null, nodes: [] }])
  })

  const dated = [
    makeNode('d1', 'X', 'Delta', '2026-07-01T00:00:00.000Z'),
    makeNode('d2', 'X', 'Charlie', '2026-07-05T00:00:00.000Z'),
    makeNode('d3', 'X', 'Bravo'), // lastModified なし（DLL経由想定）
    makeNode('d4', 'X', 'Alpha', '2026-07-03T00:00:00.000Z'),
  ]

  it('modified-asc は更新日時の古い順、日時が無いノードは末尾（displayName順）', () => {
    const groups = groupAndSortNodes(dated, 'modified-asc')
    expect(groups).toHaveLength(1)
    expect(groups[0].nodes.map(n => n.id)).toEqual(['d1', 'd4', 'd2', 'd3'])
  })

  it('modified-desc は更新日時の新しい順、日時が無いノードは末尾（displayName順）', () => {
    const groups = groupAndSortNodes(dated, 'modified-desc')
    expect(groups[0].nodes.map(n => n.id)).toEqual(['d2', 'd4', 'd1', 'd3'])
  })
})
