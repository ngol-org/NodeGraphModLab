/** Returns true when a saved graph version differs from the current node type version. */
export function isNodeVersionMismatch(
  savedVersion: string | undefined,
  currentVersion: string | undefined,
): boolean {
  if (!savedVersion) return false
  const current = currentVersion ?? '1.0.0'
  return savedVersion !== current
}

export function resolveCurrentNodeTypeVersion(currentVersion: string | undefined): string {
  return currentVersion ?? '1.0.0'
}
