// ────────────────────────────────────────────────────────────────
// Color ポート共通ユーティリティ（dataType: "color"）
// 全チャンネル 0–1 の float RGBA を正規形として扱う。
// ────────────────────────────────────────────────────────────────
export interface RgbaColor { r: number; g: number; b: number; a: number }

const clamp01 = (v: number) => Math.max(0, Math.min(1, v))

/** 0–1 の RgbaColor を CSS rgba() 文字列に変換 */
export function rgbaCssColor(c: RgbaColor): string {
  return `rgba(${Math.round(c.r * 255)},${Math.round(c.g * 255)},${Math.round(c.b * 255)},${c.a})`
}

/** unknown をできる限り RgbaColor に変換。変換不能なら fallback を返す */
export function toRgbaColor(v: unknown, fallback: RgbaColor = { r: 1, g: 1, b: 1, a: 1 }): RgbaColor {
  if (typeof v === 'object' && v !== null) {
    const obj = v as Record<string, unknown>
    if ('r' in obj || 'g' in obj || 'b' in obj) {
      const toN = (x: unknown, d = 0) => {
        const n = parseFloat(String(x))
        return isNaN(n) ? d : n
      }
      return {
        r: clamp01(toN(obj['r'])),
        g: clamp01(toN(obj['g'])),
        b: clamp01(toN(obj['b'])),
        a: clamp01(toN(obj['a'], 1)),
      }
    }
  }
  return fallback
}
