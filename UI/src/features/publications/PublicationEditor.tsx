import { useRef, useState } from 'react'
import { renderMarkdownV2 } from './sanitize'

type Mode = 'edit' | 'preview'

function insertAround(
  textarea: HTMLTextAreaElement,
  openTag: string,
  closeTag: string,
  onChange: (value: string) => void,
): void {
  const { selectionStart, selectionEnd, value } = textarea
  const selected = value.slice(selectionStart, selectionEnd)
  const before = value.slice(0, selectionStart)
  const after = value.slice(selectionEnd)

  let newValue: string
  let cursorPos: number

  if (selected.length > 0) {
    newValue = `${before}${openTag}${selected}${closeTag}${after}`
    cursorPos = selectionStart + openTag.length + selected.length + closeTag.length
  } else {
    newValue = `${before}${openTag}${closeTag}${after}`
    cursorPos = selectionStart + openTag.length
  }

  onChange(newValue)
  requestAnimationFrame(() => {
    textarea.focus()
    textarea.setSelectionRange(cursorPos, cursorPos)
  })
}

function insertLink(
  textarea: HTMLTextAreaElement,
  onChange: (value: string) => void,
): void {
  const { selectionStart, selectionEnd, value } = textarea
  const selected = value.slice(selectionStart, selectionEnd)
  const before = value.slice(0, selectionStart)
  const after = value.slice(selectionEnd)

  let newValue: string
  let selectStart: number
  let selectEnd: number

  if (selected.length > 0) {
    // Wrap selected text as display text; select placeholder URL for quick replacement
    newValue = `${before}[${selected}](url)${after}`
    selectStart = selectionStart + 1 + selected.length + 2 // start of 'url'
    selectEnd = selectStart + 3 // end of 'url'
  } else {
    // Insert [](url) and place cursor inside the brackets
    newValue = `${before}[](url)${after}`
    selectStart = selectionStart + 1
    selectEnd = selectStart
  }

  onChange(newValue)
  requestAnimationFrame(() => {
    textarea.focus()
    textarea.setSelectionRange(selectStart, selectEnd)
  })
}

interface Props {
  value: string
  onChange: (value: string) => void
  disabled: boolean
}

const TAB_ACTIVE_STYLE = {
  color: 'var(--caramel)',
  borderBottom: '2px solid var(--caramel)',
  marginBottom: '-1px',
} as const

const TAB_INACTIVE_STYLE = {
  color: '#6b7280',
  borderBottom: '2px solid transparent',
  marginBottom: '-1px',
} as const

interface ToolbarButton {
  label: string
  title: string
  action: () => void
}

export function PublicationEditor({ value, onChange, disabled }: Props) {
  const [mode, setMode] = useState<Mode>('edit')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const showToolbar = !disabled && mode === 'edit'

  const btn = (action: () => void) => () => {
    if (textareaRef.current) action()
  }

  const buttons: ToolbarButton[] = [
    {
      label: 'B',
      title: 'Bold  *text*',
      action: btn(() => insertAround(textareaRef.current!, '*', '*', onChange)),
    },
    {
      label: 'I',
      title: 'Italic  _text_',
      action: btn(() => insertAround(textareaRef.current!, '_', '_', onChange)),
    },
    {
      label: 'U',
      title: 'Underline  __text__',
      action: btn(() => insertAround(textareaRef.current!, '__', '__', onChange)),
    },
    {
      label: 'S',
      title: 'Strikethrough  ~text~',
      action: btn(() => insertAround(textareaRef.current!, '~', '~', onChange)),
    },
    {
      label: 'CODE',
      title: 'Inline code  `text`',
      action: btn(() => insertAround(textareaRef.current!, '`', '`', onChange)),
    },
    {
      label: 'PRE',
      title: 'Code block  ```text```',
      action: btn(() => insertAround(textareaRef.current!, '```\n', '\n```', onChange)),
    },
    {
      label: 'LINK',
      title: 'Link  [text](url)',
      action: btn(() => insertLink(textareaRef.current!, onChange)),
    },
    {
      label: 'SPOILER',
      title: 'Spoiler  ||text||',
      action: btn(() => insertAround(textareaRef.current!, '||', '||', onChange)),
    },
  ]

  return (
    <div>
      {/* Toolbar */}
      <div
        className="flex items-center gap-1 border-b flex-wrap px-1"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        {/* Edit / Preview tabs */}
        <button
          onClick={() => setMode('edit')}
          className="px-4 py-3 font-caps text-xs tracking-widest transition-colors"
          style={mode === 'edit' ? TAB_ACTIVE_STYLE : TAB_INACTIVE_STYLE}
          onMouseEnter={e => {
            if (mode !== 'edit') (e.currentTarget as HTMLElement).style.color = '#9ca3af'
          }}
          onMouseLeave={e => {
            if (mode !== 'edit') (e.currentTarget as HTMLElement).style.color = '#6b7280'
          }}
        >
          EDIT
        </button>
        <button
          onClick={() => setMode('preview')}
          className="px-4 py-3 font-caps text-xs tracking-widest transition-colors"
          style={mode === 'preview' ? TAB_ACTIVE_STYLE : TAB_INACTIVE_STYLE}
          onMouseEnter={e => {
            if (mode !== 'preview') (e.currentTarget as HTMLElement).style.color = '#9ca3af'
          }}
          onMouseLeave={e => {
            if (mode !== 'preview') (e.currentTarget as HTMLElement).style.color = '#6b7280'
          }}
        >
          PREVIEW
        </button>

        {/* Separator */}
        <div className="mx-2 h-5 w-px self-center" style={{ background: 'rgba(255,255,255,0.15)' }} />

        {/* Formatting buttons */}
        {buttons.map(({ label, title, action }) => (
          <button
            key={label}
            onClick={action}
            disabled={!showToolbar}
            title={title}
            className="px-3 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
            style={{
              background: 'var(--near-black)',
              borderColor: 'rgba(255,255,255,0.15)',
              color: '#9ca3af',
            }}
            onMouseEnter={e => {
              if (showToolbar) {
                e.currentTarget.style.borderColor = 'var(--caramel)'
                e.currentTarget.style.color = 'var(--caramel)'
              }
            }}
            onMouseLeave={e => {
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.15)'
              e.currentTarget.style.color = '#9ca3af'
            }}
          >
            {label}
          </button>
        ))}

        {/* Format hint */}
        <span
          className="ml-auto text-[10px] tracking-widest pr-2 self-center"
          style={{ color: 'rgba(255,255,255,0.2)' }}
        >
          TELEGRAM MARKDOWN V2
        </span>
      </div>

      {/* Edit mode */}
      {mode === 'edit' && (
        <textarea
          ref={textareaRef}
          value={value}
          onChange={e => onChange(e.target.value)}
          disabled={disabled}
          className="w-full font-mono text-sm resize-y focus:outline-none p-4"
          style={{
            minHeight: '280px',
            background: 'rgba(61,15,15,0.4)',
            border: '1px solid rgba(255,255,255,0.1)',
            borderTop: 'none',
            color: '#E8E8E8',
          }}
        />
      )}

      {/* Preview mode */}
      {mode === 'preview' && (
        <div
          data-testid="preview-pane"
          className="w-full text-sm p-4"
          style={{
            minHeight: '280px',
            background: 'rgba(61,15,15,0.4)',
            border: '1px solid rgba(255,255,255,0.1)',
            borderTop: 'none',
            color: '#E8E8E8',
            lineHeight: '1.7',
            fontFamily: 'inherit',
          }}
          dangerouslySetInnerHTML={{ __html: renderMarkdownV2(value) }}
        />
      )}
    </div>
  )
}
