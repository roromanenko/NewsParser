const ALLOWED_TAGS = new Set(['b', 'i', 'code', 'pre'])

export function sanitize(html: string): string {
  // Replace newlines before tag processing so <br /> placeholders don't get stripped
  const withBreaks = html.replace(/\n/g, '\x00BR\x00')

  let openAnchorCount = 0

  const sanitized = withBreaks.replace(/<\/?([a-zA-Z][a-zA-Z0-9]*)[^>]*>/g, (match, tag: string) => {
    const lower = tag.toLowerCase()

    if (ALLOWED_TAGS.has(lower)) {
      const isClosing = match.startsWith('</')
      return isClosing ? `</${lower}>` : `<${lower}>`
    }

    if (lower === 'a') {
      const isClosing = match.startsWith('</')
      if (isClosing) {
        if (openAnchorCount > 0) {
          openAnchorCount--
          return '</a>'
        }
        return ''
      }
      const hrefMatch = /href="([^"]*)"/.exec(match)
      if (hrefMatch) {
        openAnchorCount++
        return `<a href="${hrefMatch[1]}">`
      }
      return ''
    }

    return ''
  })

  return sanitized.replace(/\x00BR\x00/g, '<br />')
}
