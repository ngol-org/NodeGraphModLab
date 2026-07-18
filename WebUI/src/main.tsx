import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './index.css'
import { installNgolGlobalApi } from './webuiPlugin/ngolGlobalApi'
import { installDebugBridge } from './webuiPlugin/debugBridge'
import { loadExternalPlugins } from './webuiPlugin/externalPluginLoader'
import { consumeUrlToken } from './lib/authToken'

// 0. 接続認証トークン（requireAuthToken 有効時のみ意味を持つ）を URL から回収して保存
//    wsClient の最初の connect() より前に済ませる必要がある
consumeUrlToken()

// 1. 外部プラグイン向けグローバル API を構築（dynamic import より前が必須）
installNgolGlobalApi()
installDebugBridge()

// 2. 外部プラグインをロードしてから初回 render
//    （マウント時点で全レジストリ確定を保証。失敗しても finally で必ず起動する）
loadExternalPlugins().finally(() => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
})
