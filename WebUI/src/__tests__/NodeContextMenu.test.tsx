/**
 * Step01: NodeContextMenu コンポーネントテスト
 * - 単一選択モード: 既存メニュー表示
 * - 複数選択モード: 断片実行ボタン非表示、グループ化・一括削除ボタン表示
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { NodeContextMenu } from '../components/NodeContextMenu'

const DEFAULT_PROPS = {
  position: { x: 100, y: 200 },
  fragmentName: 'Fragment A',
  onExecuteFragment: vi.fn(),
  onDeleteNode: vi.fn(),
  onClose: vi.fn(),
}

describe('NodeContextMenu - 単一選択モード', () => {
  it('断片実行ボタンが表示される', () => {
    render(<NodeContextMenu {...DEFAULT_PROPS} />)
    expect(screen.getByText(/Run Fragment/)).toBeTruthy()
  })

  it('断片名ヒントが表示される', () => {
    render(<NodeContextMenu {...DEFAULT_PROPS} />)
    expect(screen.getByText('Fragment A')).toBeTruthy()
  })

  it('ノードを削除ボタンが表示される', () => {
    render(<NodeContextMenu {...DEFAULT_PROPS} />)
    expect(screen.getByText('🗑 Delete Node')).toBeTruthy()
  })

  it('ノード削除ボタンクリックで onDeleteNode と onClose が呼ばれる', () => {
    const onDeleteNode = vi.fn()
    const onClose = vi.fn()
    render(<NodeContextMenu {...DEFAULT_PROPS} onDeleteNode={onDeleteNode} onClose={onClose} />)
    fireEvent.click(screen.getByText('🗑 Delete Node'))
    expect(onDeleteNode).toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('Snapshot ノードでない場合は Snapshot 操作が表示されない', () => {
    render(<NodeContextMenu {...DEFAULT_PROPS} isSnapshotNode={false} />)
    expect(screen.queryByText(/Snapshot History/)).toBeNull()
    expect(screen.queryByText(/Clear Snapshot/)).toBeNull()
    expect(screen.queryByText(/Pin Snapshot/)).toBeNull()
  })

  it('Snapshot ノードの場合は Snapshot 操作が表示される', () => {
    render(
      <NodeContextMenu
        {...DEFAULT_PROPS}
        isSnapshotNode={true}
        hasSnapshot={true}
        isPinned={false}
        onShowHistory={vi.fn()}
        onClearSnapshot={vi.fn()}
        onTogglePin={vi.fn()}
      />
    )
    expect(screen.getByText(/Snapshot History/)).toBeTruthy()
    expect(screen.getByText(/Clear Snapshot/)).toBeTruthy()
    expect(screen.getByText(/Pin Snapshot/)).toBeTruthy()
  })

  it('Escape キーで onClose が呼ばれる', () => {
    const onClose = vi.fn()
    render(<NodeContextMenu {...DEFAULT_PROPS} onClose={onClose} />)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalled()
  })
})

describe('NodeContextMenu - 複数選択モード (selectedNodeCount >= 2)', () => {
  const MULTI_PROPS = {
    ...DEFAULT_PROPS,
    selectedNodeCount: 3,
    onDeleteSelected: vi.fn(),
    onCreateGroup: vi.fn(),
  }

  it('断片実行ボタンが表示されない', () => {
    render(<NodeContextMenu {...MULTI_PROPS} />)
    expect(screen.queryByText(/Run Fragment/)).toBeNull()
  })

  it('Snapshot 操作が表示されない', () => {
    render(<NodeContextMenu {...MULTI_PROPS} isSnapshotNode={true} />)
    expect(screen.queryByText(/Snapshot History/)).toBeNull()
    expect(screen.queryByText(/Pin Snapshot/)).toBeNull()
  })

  it('グループ化ボタンが表示される', () => {
    render(<NodeContextMenu {...MULTI_PROPS} />)
    expect(screen.getByText('🗂 Group selection')).toBeTruthy()
  })

  it('グループ化ボタンクリックで onCreateGroup と onClose が呼ばれる', () => {
    const onCreateGroup = vi.fn()
    const onClose = vi.fn()
    render(<NodeContextMenu {...MULTI_PROPS} onCreateGroup={onCreateGroup} onClose={onClose} />)
    fireEvent.click(screen.getByText('🗂 Group selection'))
    expect(onCreateGroup).toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('選択ノード数を含む削除ボタンが表示される', () => {
    render(<NodeContextMenu {...MULTI_PROPS} selectedNodeCount={3} />)
    expect(screen.getByText('🗑 Delete 3 Nodes')).toBeTruthy()
  })

  it('削除ボタンクリックで onDeleteSelected と onClose が呼ばれる', () => {
    const onDeleteSelected = vi.fn()
    const onClose = vi.fn()
    render(<NodeContextMenu {...MULTI_PROPS} onDeleteSelected={onDeleteSelected} onClose={onClose} />)
    fireEvent.click(screen.getByText('🗑 Delete 3 Nodes'))
    expect(onDeleteSelected).toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('onCreateGroup が未指定の場合はグループ化ボタンが表示されない', () => {
    const { onCreateGroup: _, ...propsWithoutGroup } = MULTI_PROPS
    render(<NodeContextMenu {...propsWithoutGroup} />)
    expect(screen.queryByText('🗂 Group selection')).toBeNull()
  })

  it('selectedNodeCount=1 の場合は単一選択モードになる', () => {
    render(<NodeContextMenu {...MULTI_PROPS} selectedNodeCount={1} />)
    expect(screen.getByText(/Run Fragment/)).toBeTruthy()
    expect(screen.queryByText(/Delete \d+ Nodes/)).toBeNull()
  })
})
