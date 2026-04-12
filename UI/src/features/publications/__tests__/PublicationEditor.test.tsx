import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PublicationEditor } from '../PublicationEditor'

// requestAnimationFrame is not available in jsdom — provide a synchronous stub
beforeEach(() => {
  vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
    cb(0)
    return 0
  })
})

describe('PublicationEditor — EDIT / PREVIEW toggle', () => {
  it('renders in EDIT mode by default, showing a textarea', () => {
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.queryByTestId('preview-pane')).not.toBeInTheDocument()
  })

  it('switches to PREVIEW mode when the PREVIEW button is clicked', async () => {
    const user = userEvent.setup()
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(screen.getByTestId('preview-pane')).toBeInTheDocument()
  })

  it('switches back to EDIT mode when the EDIT button is clicked from PREVIEW', async () => {
    const user = userEvent.setup()
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))
    await user.click(screen.getByRole('button', { name: 'EDIT' }))

    expect(screen.getByRole('textbox')).toBeInTheDocument()
  })
})

describe('PublicationEditor — disabled state', () => {
  it('disables the textarea when the disabled prop is true', () => {
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={true} />)

    expect(screen.getByRole('textbox')).toBeDisabled()
  })

  it('disables all toolbar formatting buttons when disabled prop is true', () => {
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={true} />)

    const formattingButtons = ['B', 'I', 'U', 'S', 'CODE', 'PRE', 'LINK', 'SPOILER']
    formattingButtons.forEach(label => {
      expect(screen.getByRole('button', { name: label })).toBeDisabled()
    })
  })

  it('disables formatting buttons when in PREVIEW mode even if not disabled', async () => {
    const user = userEvent.setup()
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={false} />)

    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))

    expect(screen.getByRole('button', { name: 'B' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'I' })).toBeDisabled()
  })

  it('enables formatting buttons when in EDIT mode and not disabled', () => {
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={false} />)

    const formattingButtons = ['B', 'I', 'U', 'S', 'CODE', 'PRE', 'LINK', 'SPOILER']
    formattingButtons.forEach(label => {
      expect(screen.getByRole('button', { name: label })).not.toBeDisabled()
    })
  })
})

describe('PublicationEditor — toolbar button interactions (MarkdownV2 insertion)', () => {
  it('wraps selected text with *...* when B button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    fireEvent.select(textarea, { target: { selectionStart: 6, selectionEnd: 11 } })
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    await user.click(screen.getByRole('button', { name: 'B' }))

    expect(handleChange).toHaveBeenCalledWith('hello *world*')
  })

  it('inserts empty ** markers at cursor when B is clicked with no selection', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 5
    textarea.selectionEnd = 5

    await user.click(screen.getByRole('button', { name: 'B' }))

    expect(handleChange).toHaveBeenCalledWith('hello**')
  })

  it('wraps selected text with _..._ when I button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    await user.click(screen.getByRole('button', { name: 'I' }))

    expect(handleChange).toHaveBeenCalledWith('hello _world_')
  })

  it('wraps selected text with `...` when CODE button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="some code" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 5
    textarea.selectionEnd = 9

    await user.click(screen.getByRole('button', { name: 'CODE' }))

    expect(handleChange).toHaveBeenCalledWith('some `code`')
  })

  it('wraps selected text with ```\\n...\\n``` when PRE button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="some code" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 5
    textarea.selectionEnd = 9

    await user.click(screen.getByRole('button', { name: 'PRE' }))

    expect(handleChange).toHaveBeenCalledWith('some ```\ncode\n```')
  })

  it('wraps selected text with ~...~ when S button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    await user.click(screen.getByRole('button', { name: 'S' }))

    expect(handleChange).toHaveBeenCalledWith('hello ~world~')
  })

  it('wraps selected text with __...__ when U button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    await user.click(screen.getByRole('button', { name: 'U' }))

    expect(handleChange).toHaveBeenCalledWith('hello __world__')
  })

  it('wraps selected text with ||...|| when SPOILER button is clicked', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    await user.click(screen.getByRole('button', { name: 'SPOILER' }))

    expect(handleChange).toHaveBeenCalledWith('hello ||world||')
  })

  it('inserts [](url) at cursor when LINK is clicked with no selection', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="text" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 4
    textarea.selectionEnd = 4

    await user.click(screen.getByRole('button', { name: 'LINK' }))

    expect(handleChange).toHaveBeenCalledWith('text[](url)')
  })

  it('wraps selected text as link display text when LINK is clicked with selection', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="click here now" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 6
    textarea.selectionEnd = 10 // "here"

    await user.click(screen.getByRole('button', { name: 'LINK' }))

    expect(handleChange).toHaveBeenCalledWith('click [here](url) now')
  })

  it('calls onChange when text is typed in the textarea', async () => {
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="" onChange={handleChange} disabled={false} />)

    await user.type(screen.getByRole('textbox'), 'x')

    expect(handleChange).toHaveBeenCalled()
  })
})
