import { describe, expect, it } from 'vitest'
import { isNodeVersionMismatch, resolveCurrentNodeTypeVersion } from '../lib/nodeVersion'

describe('nodeVersion', () => {
  it('resolveCurrentNodeTypeVersion defaults to 1.0.0', () => {
    expect(resolveCurrentNodeTypeVersion(undefined)).toBe('1.0.0')
    expect(resolveCurrentNodeTypeVersion('2.0.0')).toBe('2.0.0')
  })

  it('isNodeVersionMismatch is false when saved version is missing', () => {
    expect(isNodeVersionMismatch(undefined, '2.0.0')).toBe(false)
  })

  it('isNodeVersionMismatch is false when versions match', () => {
    expect(isNodeVersionMismatch('1.0.0', '1.0.0')).toBe(false)
    expect(isNodeVersionMismatch('2.0.0', '2.0.0')).toBe(false)
  })

  it('isNodeVersionMismatch is true when versions differ', () => {
    expect(isNodeVersionMismatch('1.0.0', '2.0.0')).toBe(true)
    expect(isNodeVersionMismatch('2.0.0', undefined)).toBe(true)
  })
})
