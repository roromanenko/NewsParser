function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/** Renders Telegram MarkdownV2 to HTML for preview display. */
export function renderMarkdownV2(md: string): string {
  // Protect escaped characters from pattern matching
  const PLACEHOLDER = '\x00'
  const escaped: string[] = []
  let result = md.replace(/\\([_*[\]()~`>#+=|{}.!\\-])/g, (_, ch) => {
    escaped.push(escapeHtml(ch))
    return `${PLACEHOLDER}${escaped.length - 1}${PLACEHOLDER}`
  })

  // HTML-escape remaining special chars
  result = result
    .split(PLACEHOLDER)
    .map((part, i) => (i % 2 === 0 ? escapeHtml(part) : part))
    .join(PLACEHOLDER)

  // Code blocks (``` ... ```) — must come before inline code
  result = result.replace(/```([\s\S]*?)```/g, (_, code) =>
    `<pre><code>${code.trim()}</code></pre>`,
  )

  // Inline code
  result = result.replace(/`([^`\n]+)`/g, '<code>$1</code>')

  // Bold: *text*
  result = result.replace(/\*([^*\n]+)\*/g, '<b>$1</b>')

  // Underline: __text__ — must come before italic _
  result = result.replace(/__([^_\n]+)__/g, '<u>$1</u>')

  // Italic: _text_
  result = result.replace(/_([^_\n]+)_/g, '<i>$1</i>')

  // Strikethrough: ~text~
  result = result.replace(/~([^~\n]+)~/g, '<s>$1</s>')

  // Spoiler: ||text||
  result = result.replace(
    /\|\|([^|\n]+)\|\|/g,
    '<span style="background:#4a4a4a;color:transparent;border-radius:2px;cursor:pointer" title="spoiler">$1</span>',
  )

  // Links: [text](url)
  result = result.replace(
    /\[([^\]]+)\]\(([^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>',
  )

  // Restore escaped characters
  result = result.replace(
    new RegExp(`${PLACEHOLDER}(\\d+)${PLACEHOLDER}`, 'g'),
    (_, i) => escaped[Number(i)],
  )

  // Newlines → <br />
  result = result.replace(/\n/g, '<br />')

  return result
}
