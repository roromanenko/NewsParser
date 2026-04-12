import { describe, it, expect } from 'vitest'
import { sanitize } from '../sanitize'

describe('sanitize', () => {
  // P0 — allowed inline tags pass through
  it('preserves <b> tags', () => {
    // Arrange
    const input = '<b>bold text</b>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<b>bold text</b>')
  })

  it('preserves <i> tags', () => {
    // Arrange
    const input = '<i>italic text</i>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<i>italic text</i>')
  })

  it('preserves <code> tags', () => {
    // Arrange
    const input = '<code>const x = 1</code>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<code>const x = 1</code>')
  })

  it('preserves <pre> tags', () => {
    // Arrange
    const input = '<pre>  indented</pre>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<pre>  indented</pre>')
  })

  // P0 — <a> with href preserved; attributes beyond href stripped
  it('preserves <a href="..."> tags, keeping the href value', () => {
    // Arrange
    const input = '<a href="https://example.com">link</a>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<a href="https://example.com">link</a>')
  })

  it('strips extra attributes from <a>, keeping only href', () => {
    // Arrange
    const input = '<a href="https://example.com" target="_blank" rel="noopener">link</a>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<a href="https://example.com">link</a>')
  })

  // P1 — <a> without href is stripped entirely
  it('strips <a> tags that have no href attribute', () => {
    // Arrange
    const input = '<a name="anchor">anchor</a>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('anchor')
  })

  // P0 — disallowed tags are removed, text content survives
  it('strips disallowed tags such as <div>', () => {
    // Arrange
    const input = '<div>content</div>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('content')
  })

  it('strips <script> tags', () => {
    // Arrange
    const input = '<script>alert("xss")</script>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('alert("xss")')
  })

  it('strips <span> tags while keeping inner text', () => {
    // Arrange
    const input = '<span class="highlight">text</span>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('text')
  })

  // P0 — newline conversion
  it('converts newline characters to <br /> tags', () => {
    // Arrange
    const input = 'line one\nline two'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('line one<br />line two')
  })

  it('converts multiple newlines to multiple <br /> tags', () => {
    // Arrange
    const input = 'a\n\nb'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('a<br /><br />b')
  })

  // P2 — edge cases
  it('returns empty string unchanged', () => {
    // Arrange & Act
    const result = sanitize('')

    // Assert
    expect(result).toBe('')
  })

  it('passes through plain text with no tags or newlines', () => {
    // Arrange
    const input = 'plain text without markup'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('plain text without markup')
  })

  it('handles mixed allowed and disallowed tags in the same string', () => {
    // Arrange
    const input = '<div><b>bold</b> and <script>evil()</script></div>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<b>bold</b> and evil()')
  })

  it('normalises uppercase tag names to lowercase for allowed tags', () => {
    // Arrange
    const input = '<B>text</B>'

    // Act
    const result = sanitize(input)

    // Assert
    expect(result).toBe('<b>text</b>')
  })
})
