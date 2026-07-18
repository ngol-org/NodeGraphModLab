/**
 *  NodeAddMenu コンポーネントテスト
 * - 検索フィルター
 * - カテゴリグループ表示
 * - Enterキーで最初のマッチを選択
 * - Escapeキーで閉じる
 * - 閉じるボタンで閉じる
 * - ノードクリックでonAdd呼び出し
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { NodeAddMenu } from '../components/NodeAddMenu'
import type { NodeTypeInfo } from '../types/protocol'

const SAMPLE_NODES: NodeTypeInfo[] = [
  {
    id: 'ngol.logic.add',
    category: 'Logic',
    displayName: '加算',
    ports: [],
    description: 'Add two numbers',
  },
  {
    id: 'ngol.logic.foreach',
    category: 'Logic',
    displayName: 'ForEach',
    ports: [],
    description: 'Iterate items',
  },
  {
    id: 'ngol.snapshot.get_value',
    category: 'Snapshot',
    displayName: 'GetSnapshotValue',
    ports: [],
    description: 'Get the current value from a snapshot slot',
  },
]

const DEFAULT_POS = { x: 100, y: 200 }

describe('NodeAddMenu', () => {
  it('ノード一覧が表示される', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    expect(screen.getByText('加算')).toBeTruthy()
    expect(screen.getByText('ForEach')).toBeTruthy()
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
  })

  it('カテゴリラベルが表示される', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    expect(screen.getByText('Logic')).toBeTruthy()
    expect(screen.getByText('Snapshot')).toBeTruthy()
  })

  it('検索クエリでフィルタリングされる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'foreach' } })
    expect(screen.getByText('ForEach')).toBeTruthy()
    expect(screen.queryByText('加算')).toBeNull()
    expect(screen.queryByText('GetSnapshotValue')).toBeNull()
  })

  it('カテゴリ名で検索できる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'snapshot' } })
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
    expect(screen.queryByText('加算')).toBeNull()
  })

  it('マッチなしの場合に "No nodes found" 表示', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'zzzznonexistent' } })
    expect(screen.getByText('No nodes found')).toBeTruthy()
  })

  it('ノードをクリックするとonAddが呼ばれる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    fireEvent.click(screen.getByText('加算'))
    expect(onAdd).toHaveBeenCalledWith('ngol.logic.add')
  })

  it('閉じるボタンでonCloseが呼ばれる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    fireEvent.click(screen.getByTitle('Close'))
    expect(onClose).toHaveBeenCalled()
  })

  it('EscapeキーでonCloseが呼ばれる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(onClose).toHaveBeenCalled()
  })

  it('検索結果が1件のときEnterキーでonAddが呼ばれる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'foreach' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onAdd).toHaveBeenCalledWith('ngol.logic.foreach')
  })

  it('ArrowDownで1つ目のアイテムを選択しEnterで追加できる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'logic' } })
    // 2件ヒット(加算, ForEach) → ArrowDown 1回 → index=0(加算)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onAdd).toHaveBeenCalledWith('ngol.logic.add')
  })

  it('ArrowDown×2で2つ目のアイテムを選択しEnterで追加できる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'logic' } })
    // 2件ヒット(加算, ForEach) → ArrowDown 2回 → index=1(ForEach)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onAdd).toHaveBeenCalledWith('ngol.logic.foreach')
  })

  it('ArrowDown後ArrowUpで1つ目に戻りEnterで追加できる', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'logic' } })
    // Down×2 → index=1, Up×1 → index=0(加算)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'ArrowUp' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onAdd).toHaveBeenCalledWith('ngol.logic.add')
  })

  it('選択されたアイテムに--selectedクラスが付く', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    const { container } = render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'logic' } })
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    const selected = container.querySelectorAll('.node-add-menu__item--selected')
    expect(selected.length).toBe(1)
  })

  it('メニューの位置が正しく設定される', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    const { container } = render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={{ x: 300, y: 400 }}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const menu = container.querySelector('.node-add-menu') as HTMLElement
    expect(menu.style.left).toBe('300px')
    expect(menu.style.top).toBe('400px')
  })

  // ── fuzzy 検索テスト ──────────────────────────────────────────
  it('fuzzy: "gsv" → GetSnapshotValue にマッチ', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'gsv' } })
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
    expect(screen.queryByText('加算')).toBeNull()
  })

  it('fuzzy: "gsval" → GetSnapshotValue にマッチ（displayName fuzzy）', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'gsval' } })
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
  })

  it('fuzzy: id のみにマッチするクエリは displayName fuzzy ではマッチしない', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    // "lgcadd" は id=ngol.logic.add の fuzzy だが displayName="加算" には fuzzy しない
    // → category/id/description の includes にもマッチしないのでヒットしない
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'lgcadd' } })
    expect(screen.queryByText('加算')).toBeNull()
  })

  it('fuzzy: 対象にない文字列はマッチしない', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'zzzxxx' } })
    expect(screen.getByText('No nodes found')).toBeTruthy()
  })

  // ── スペース区切り AND 検索テスト ────────────────────────────
  it('AND検索: "snapshot get" → GetSnapshotValue にマッチ', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'snapshot get' } })
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
    expect(screen.queryByText('加算')).toBeNull()
  })

  it('AND検索: "get snapshot" → GetSnapshotValue にマッチ（逆順でも可）', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'get snapshot' } })
    expect(screen.getByText('GetSnapshotValue')).toBeTruthy()
    expect(screen.queryByText('加算')).toBeNull()
  })

  it('AND検索: "logic add" → 加算 にマッチ', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'logic add' } })
    expect(screen.getByText('加算')).toBeTruthy()
    expect(screen.queryByText('GetSnapshotValue')).toBeNull()
  })

  it('AND検索: 片方のトークンが存在しない場合はマッチしない', () => {
    const onAdd = vi.fn()
    const onClose = vi.fn()
    render(
      <NodeAddMenu
        nodeTypes={SAMPLE_NODES}
        position={DEFAULT_POS}
        onAdd={onAdd}
        onClose={onClose}
      />
    )
    const input = screen.getByPlaceholderText(/Search nodes/)
    fireEvent.change(input, { target: { value: 'snapshot zzzzz' } })
    expect(screen.getByText('No nodes found')).toBeTruthy()
  })
})
