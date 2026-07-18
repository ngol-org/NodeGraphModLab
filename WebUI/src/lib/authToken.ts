const STORAGE_KEY = 'ngol_auth_token'

/** requireAuthToken 有効時のみ使用される。無効時は常に null で、wsClient は無トークンで接続する。 */
export function getAuthToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}

export function setAuthToken(token: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, token)
  } catch {
    /* localStorage が使えない環境（プライベートモード等）では無視 */
  }
}

/**
 * 起動用URL（`http://localhost:PORT/?token=xxx`）の token クエリパラメータを読み取り、
 * localStorage へ保存した上で URL から除去する（ブラウザ履歴・共有URLに残さないため）。
 * 以後の WebSocket 接続は wsClient がこの保存済みトークンをサブプロトコルとして使用する。
 */
export function consumeUrlToken(): void {
  if (typeof window === 'undefined') return
  const params = new URLSearchParams(window.location.search)
  const token = params.get('token')
  if (!token) return

  setAuthToken(token)

  params.delete('token')
  const query = params.toString()
  const newUrl = window.location.pathname + (query ? `?${query}` : '') + window.location.hash
  window.history.replaceState({}, '', newUrl)
}
