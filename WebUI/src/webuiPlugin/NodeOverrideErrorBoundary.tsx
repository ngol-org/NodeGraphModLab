import React from 'react'

interface Props {
  /** エラーログ用のノード型 ID。 */
  nodeTypeId: string
  /** 描画例外の発生を親（CustomNode）へ通知する。親は標準描画へ切り替える。 */
  onError: () => void
  children: React.ReactNode
}

interface State {
  hasError: boolean
}

/**
 * ノード型 ID 上書き専用の Error Boundary
 *
 * PluginErrorBoundary はエラー表示を出すが、型 ID 上書きは
 * 「ノード作者が選んでいない第三者 UI」であるため、失敗時はエラー表示ではなく
 * 標準描画へ自動フォールバックさせる（ガードレール G3）。
 * ここでは onError で親に通知するだけで、フォールバック描画自体は親が行う。
 */
export class NodeOverrideErrorBoundary extends React.Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false }
  }

  static getDerivedStateFromError(): State {
    return { hasError: true }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo): void {
    console.error(
      `[NGOL Plugin] Node type override render error for '${this.props.nodeTypeId}' — falling back to standard UI:`,
      error,
      info.componentStack
    )
    this.props.onError()
  }

  render(): React.ReactNode {
    // 親の再描画（標準描画への切り替え）までの一瞬だけ空表示にする
    if (this.state.hasError) return null
    return this.props.children
  }
}
