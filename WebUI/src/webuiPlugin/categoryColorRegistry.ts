const categoryColors = new Map<string, string>()

/** プラグインからノードカテゴリのアクセントカラーを登録する。同一カテゴリの再登録は上書き。 */
export function registerCategoryColor(category: string, color: string): void {
  categoryColors.set(category, color)
}

/** プラグイン登録済みのカテゴリカラーを返す。未登録なら undefined。 */
export function getPluginCategoryColor(category: string): string | undefined {
  return categoryColors.get(category)
}
