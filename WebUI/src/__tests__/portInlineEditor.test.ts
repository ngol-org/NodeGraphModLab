import { describe, expect, it } from 'vitest'
import { portSupportsInlineEditor, shouldShowPortInlineEditor } from '../lib/portInlineEditor'

describe('portSupportsInlineEditor', () => {
  it('supports the known inline-editable types', () => {
    expect(portSupportsInlineEditor('boolean')).toBe(true)
    expect(portSupportsInlineEditor('bool')).toBe(true)
    expect(portSupportsInlineEditor('number')).toBe(true)
    expect(portSupportsInlineEditor('int')).toBe(true)
    expect(portSupportsInlineEditor('string')).toBe(true)
    expect(portSupportsInlineEditor('enum:A|B|C')).toBe(true)
    expect(portSupportsInlineEditor('color')).toBe(true)
  })

  it('rejects unsupported types', () => {
    expect(portSupportsInlineEditor('gameobject')).toBe(false)
    expect(portSupportsInlineEditor('any')).toBe(false)
    expect(portSupportsInlineEditor('vector3')).toBe(false)
  })
})

describe('shouldShowPortInlineEditor', () => {
  const inputPort = (dataType: string, showInlineEditor?: boolean | null) => ({
    dataType,
    showInlineEditor,
    direction: 'input' as const,
  })
  const outputPort = (dataType: string, showInlineEditor?: boolean | null) => ({
    dataType,
    showInlineEditor,
    direction: 'output' as const,
  })

  it('hides when showInlineEditor is null/undefined (opt-in default)', () => {
    expect(shouldShowPortInlineEditor(inputPort('number', null), 0)).toBe(false)
    expect(shouldShowPortInlineEditor(inputPort('number', undefined), 0)).toBe(false)
  })

  it('hides when showInlineEditor is explicitly false', () => {
    expect(shouldShowPortInlineEditor(inputPort('number', false), 0)).toBe(false)
  })

  it('shows when showInlineEditor is true and type is supported', () => {
    expect(shouldShowPortInlineEditor(inputPort('string', true), 0)).toBe(true)
    expect(shouldShowPortInlineEditor(inputPort('number', true), 1)).toBe(true)
  })

  it('hides when showInlineEditor is true but type is unsupported', () => {
    expect(shouldShowPortInlineEditor(inputPort('gameobject', true), 0)).toBe(false)
  })

  it('output port with showInlineEditor=true is hidden when the node has input ports', () => {
    expect(shouldShowPortInlineEditor(outputPort('number', true), 1)).toBe(false)
  })

  it('output port with showInlineEditor=true is shown when the node has no input ports', () => {
    expect(shouldShowPortInlineEditor(outputPort('number', true), 0)).toBe(true)
  })
})
