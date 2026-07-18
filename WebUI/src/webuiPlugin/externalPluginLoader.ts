import { NGOL_API_VERSION } from './ngolGlobalApi'

/** GET /api/webui-plugins が返すマニフェストの 1 エントリ。 */
interface PluginManifestEntry {
  id: string
  displayName?: string | null
  version?: string | null
  scriptUrl: string
  apiVersion?: number | null
}

/** マニフェスト取得のタイムアウト（ms）。サーバー無応答でも WebUI 起動を止めない。 */
const MANIFEST_TIMEOUT_MS = 3000

/**
 * サーバーから外部プラグインのマニフェストを取得し、各 .js を dynamic import する。
 * プラグインは import 時の side-effect として window.NGOL.register*() を呼ぶ。
 *
 * - マニフェスト取得失敗・404・不正 JSON → 外部プラグインなしとして正常継続
 * - 個別プラグインの import 失敗 → warn ログのみで残りを継続
 *
 * 必ず installNgolGlobalApi() の後、React の初回 render より前に await すること
 * （render 前に全レジストリ確定を保証するため）。
 */
export async function loadExternalPlugins(): Promise<void> {
  let entries: PluginManifestEntry[]
  try {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), MANIFEST_TIMEOUT_MS)
    const res = await fetch('/api/webui-plugins', { signal: controller.signal })
    clearTimeout(timer)
    if (!res.ok) return
    const parsed: unknown = await res.json()
    if (!Array.isArray(parsed)) return
    entries = parsed as PluginManifestEntry[]
  } catch {
    // Vite dev サーバー（エンドポイントなし）や旧バージョンサーバーでは正常系
    return
  }

  if (entries.length === 0) return

  await Promise.allSettled(
    entries.map(async (entry) => {
      try {
        if (entry.apiVersion != null && entry.apiVersion !== NGOL_API_VERSION) {
          console.warn(
            `[NGOL Plugin] '${entry.id}' declares apiVersion ${entry.apiVersion} ` +
            `(host is ${NGOL_API_VERSION}) — loading anyway`
          )
        }
        await import(/* @vite-ignore */ entry.scriptUrl)
        console.log(`[NGOL Plugin] Loaded: ${entry.id}${entry.version ? ' v' + entry.version : ''}`)
      } catch (e) {
        console.warn(`[NGOL Plugin] Failed to load '${entry.id}' (${entry.scriptUrl}):`, e)
      }
    })
  )
}
