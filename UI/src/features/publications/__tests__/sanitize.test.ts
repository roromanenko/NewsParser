import { describe, it, expect } from 'vitest'
import { renderMarkdownV2 } from '../sanitize'

describe('renderMarkdownV2 — bold, italic, underline, strikethrough', () => {
  it('renders *text* as <b>', () => {
    expect(renderMarkdownV2('*bold*')).toBe('<b>bold</b>')
  })

  it('renders _text_ as <i>', () => {
    expect(renderMarkdownV2('_italic_')).toBe('<i>italic</i>')
  })

  it('renders __text__ as <u>', () => {
    expect(renderMarkdownV2('__underline__')).toBe('<u>underline</u>')
  })

  it('renders __text__ before _text_ so underline takes precedence', () => {
    expect(renderMarkdownV2('__u__ and _i_')).toBe('<u>u</u> and <i>i</i>')
  })

  it('renders ~text~ as <s>', () => {
    expect(renderMarkdownV2('~strike~')).toBe('<s>strike</s>')
  })
})

describe('renderMarkdownV2 — code', () => {
  it('renders `text` as <code>', () => {
    expect(renderMarkdownV2('`code`')).toBe('<code>code</code>')
  })

  it('renders ```block``` as <pre><code>', () => {
    expect(renderMarkdownV2('```\nconsole.log()\n```')).toBe(
      '<pre><code>console.log()</code></pre>',
    )
  })

  it('code block takes precedence over inline code matching', () => {
    const input = '```\nfoo\n```'
    expect(renderMarkdownV2(input)).toContain('<pre><code>')
    expect(renderMarkdownV2(input)).not.toContain('<code>foo</code><br />')
  })
})

describe('renderMarkdownV2 — links', () => {
  it('renders [text](url) as <a href>', () => {
    expect(renderMarkdownV2('[click](https://example.com)')).toBe(
      '<a href="https://example.com" target="_blank" rel="noopener noreferrer">click</a>',
    )
  })
})

describe('renderMarkdownV2 — spoiler', () => {
  it('renders ||text|| as a span with transparent color', () => {
    const result = renderMarkdownV2('||secret||')
    expect(result).toContain('<span')
    expect(result).toContain('secret')
    expect(result).toContain('color:transparent')
  })
})

describe('renderMarkdownV2 — HTML escaping', () => {
  it('escapes < and > in plain text', () => {
    expect(renderMarkdownV2('a < b > c')).toBe('a &lt; b &gt; c')
  })

  it('escapes & in plain text', () => {
    expect(renderMarkdownV2('foo & bar')).toBe('foo &amp; bar')
  })

  it('does not double-escape HTML inside code blocks', () => {
    const result = renderMarkdownV2('```\n<div>\n```')
    expect(result).toContain('&lt;div&gt;')
  })
})

describe('renderMarkdownV2 — escape sequences', () => {
  it('renders \\* as a literal asterisk', () => {
    expect(renderMarkdownV2('\\*not bold\\*')).toBe('*not bold*')
  })

  it('renders \\_ as a literal underscore', () => {
    expect(renderMarkdownV2('\\_not italic\\_')).toBe('_not italic_')
  })
})

describe('renderMarkdownV2 — newlines', () => {
  it('converts newlines to <br />', () => {
    expect(renderMarkdownV2('line one\nline two')).toBe('line one<br />line two')
  })

  it('converts multiple consecutive newlines', () => {
    expect(renderMarkdownV2('a\n\nb')).toBe('a<br /><br />b')
  })
})

describe('renderMarkdownV2 — edge cases', () => {
  it('returns empty string unchanged', () => {
    expect(renderMarkdownV2('')).toBe('')
  })

  it('passes through plain text with no markup', () => {
    expect(renderMarkdownV2('plain text')).toBe('plain text')
  })

  it('renders mixed formatting in the same string', () => {
    const result = renderMarkdownV2('*bold* and _italic_')
    expect(result).toBe('<b>bold</b> and <i>italic</i>')
  })
})
