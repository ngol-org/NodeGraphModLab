import React from 'react'

interface Props {
  /** エラーログ用のプラグイン識別子。 */
  pluginId: string
  children: React.ReactNode
}

interface State {
  hasError: boolean
}

/**
 * プラグイン描画例外を隔離する Error Boundary。
 * 壊れたプラグインコンポーネントがエディタ全体を落とさないよう、
 * プラグイン描画（widget / nodeRenderer / panel）は必ずこれで包む。
 */
export class PluginErrorBoundary extends React.Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false }
  }

  static getDerivedStateFromError(): State {
    return { hasError: true }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo): void {
    console.error(`[NGOL Plugin] Render error in '${this.props.pluginId}':`, error, info.componentStack)
  }

  render(): React.ReactNode {
    if (this.state.hasError) {
      return (
        <div
          style={{
            padding: '4px 8px',
            fontSize: '11px',
            color: '#f48771',
            border: '1px dashed #f48771',
            borderRadius: '4px',
          }}
        >
          Plugin error: {this.props.pluginId}
        </div>
      )
    }
    return this.props.children
  }
}
