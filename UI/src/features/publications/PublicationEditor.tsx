import { useRef, useState } from 'react'
import { sanitize } from './sanitize'

type Mode = 'edit' | 'preview'

function insertTag(
  textarea: HTMLTextAreaElement,
  openTag: string,
  closeTag: string,
  onChange: (value: string) => void
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
  onChange: (value: string) => void
): void {
  const { selectionStart, value } = textarea
  const before = value.slice(0, selectionStart)
  const after = value.slice(selectionStart)
  const tag = '<a href=""></a>'
  const newValue = `${before}${tag}${after}`
  const cursorPos = selectionStart + 9 // places cursor inside href=""

  onChange(newValue)

  requestAnimationFrame(() => {
    textarea.focus()
    textarea.setSelectionRange(cursorPos, cursorPos)
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

export function PublicationEditor({ value, onChange, disabled }: Props) {
  const [mode, setMode] = useState<Mode>('edit')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const showToolbar = !disabled && mode === 'edit'

  const handleBold = () => {
    if (textareaRef.current) insertTag(textareaRef.current, '<b>', '</b>', onChange)
  }

  const handleItalic = () => {
    if (textareaRef.current) insertTag(textareaRef.current, '<i>', '</i>', onChange)
  }

  const handleCode = () => {
    if (textareaRef.current) insertTag(textareaRef.current, '<code>', '</code>', onChange)
  }

  const handleLink = () => {
    if (textareaRef.current) insertLink(textareaRef.current, onChange)
  }

  return (
    <div>
      {/* Toolbar */}
      <div
        className="flex items-center gap-1 border-b mb-0 flex-wrap"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        {/* Edit / Preview toggle */}
        <button
          onClick={() => setMode('edit')}
          className="px-4 py-2.5 font-caps text-xs tracking-widest transition-colors"
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
          className="px-4 py-2.5 font-caps text-xs tracking-widest transition-colors"
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
        <div className="mx-2 h-4 w-px" style={{ background: 'rgba(255,255,255,0.1)' }} />

        {/* Formatting buttons */}
        {(['B', 'I', 'LINK', 'CODE'] as const).map(label => (
          <button
            key={label}
            onClick={() => {
              if (label === 'B') handleBold()
              else if (label === 'I') handleItalic()
              else if (label === 'LINK') handleLink()
              else handleCode()
            }}
            disabled={!showToolbar}
            className="px-2.5 py-1.5 font-caps text-[10px] tracking-widest border transition-colors disabled:opacity-30"
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
          className="w-full font-mono text-sm p-4"
          style={{
            minHeight: '280px',
            background: 'rgba(61,15,15,0.4)',
            border: '1px solid rgba(255,255,255,0.1)',
            borderTop: 'none',
            color: '#E8E8E8',
            lineHeight: '1.7',
          }}
          dangerouslySetInnerHTML={{ __html: sanitize(value) }}
        />
      )}
    </div>
  )
}
