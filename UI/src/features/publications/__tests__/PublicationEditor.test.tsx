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
    // Arrange & Act
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    // Assert
    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.queryByTestId('preview-pane')).not.toBeInTheDocument()
  })

  it('switches to PREVIEW mode when the PREVIEW button is clicked', async () => {
    // Arrange
    const user = userEvent.setup()
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    // Act
    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))

    // Assert
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    // The preview div contains the sanitized content
    expect(screen.getByText('hello')).toBeInTheDocument()
  })

  it('switches back to EDIT mode when the EDIT button is clicked from PREVIEW', async () => {
    // Arrange
    const user = userEvent.setup()
    render(<PublicationEditor value="hello" onChange={vi.fn()} disabled={false} />)

    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'EDIT' }))

    // Assert
    expect(screen.getByRole('textbox')).toBeInTheDocument()
  })
})

describe('PublicationEditor — disabled state', () => {
  it('disables the textarea when the disabled prop is true', () => {
    // Arrange & Act
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={true} />)

    // Assert
    expect(screen.getByRole('textbox')).toBeDisabled()
  })

  it('disables all toolbar formatting buttons when disabled prop is true', () => {
    // Arrange & Act
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={true} />)

    // Assert — B, I, LINK, CODE buttons should all be disabled
    const formattingButtons = ['B', 'I', 'LINK', 'CODE']
    formattingButtons.forEach(label => {
      expect(screen.getByRole('button', { name: label })).toBeDisabled()
    })
  })

  it('disables formatting buttons when in PREVIEW mode even if not disabled', async () => {
    // Arrange
    const user = userEvent.setup()
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={false} />)

    // Act — switch to preview
    await user.click(screen.getByRole('button', { name: 'PREVIEW' }))

    // Assert — formatting buttons are disabled because showToolbar = false in preview mode
    expect(screen.getByRole('button', { name: 'B' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'I' })).toBeDisabled()
  })

  it('enables formatting buttons when in EDIT mode and not disabled', () => {
    // Arrange & Act
    render(<PublicationEditor value="content" onChange={vi.fn()} disabled={false} />)

    // Assert
    const formattingButtons = ['B', 'I', 'LINK', 'CODE']
    formattingButtons.forEach(label => {
      expect(screen.getByRole('button', { name: label })).not.toBeDisabled()
    })
  })
})

describe('PublicationEditor — toolbar button interactions (tag insertion)', () => {
  it('wraps selected text with <b>...</b> when B button is clicked', async () => {
    // Arrange
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello world" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement

    // Simulate selection of "world" (positions 6–11)
    fireEvent.select(textarea, { target: { selectionStart: 6, selectionEnd: 11 } })
    textarea.selectionStart = 6
    textarea.selectionEnd = 11

    // Act
    await user.click(screen.getByRole('button', { name: 'B' }))

    // Assert
    expect(handleChange).toHaveBeenCalledWith('hello <b>world</b>')
  })

  it('inserts empty <i></i> tags at cursor when no text is selected', async () => {
    // Arrange
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="hello" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 5
    textarea.selectionEnd = 5

    // Act
    await user.click(screen.getByRole('button', { name: 'I' }))

    // Assert
    expect(handleChange).toHaveBeenCalledWith('hello<i></i>')
  })

  it('wraps selected text with <code>...</code> when CODE button is clicked', async () => {
    // Arrange
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="some code" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 5
    textarea.selectionEnd = 9

    // Act
    await user.click(screen.getByRole('button', { name: 'CODE' }))

    // Assert
    expect(handleChange).toHaveBeenCalledWith('some <code>code</code>')
  })

  it('inserts <a href=""></a> template at cursor when LINK button is clicked', async () => {
    // Arrange
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="text" onChange={handleChange} disabled={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    textarea.selectionStart = 4
    textarea.selectionEnd = 4

    // Act
    await user.click(screen.getByRole('button', { name: 'LINK' }))

    // Assert
    expect(handleChange).toHaveBeenCalledWith('text<a href=""></a>')
  })

  it('calls onChange with updated value when text is typed in the textarea', async () => {
    // Arrange
    const user = userEvent.setup()
    const handleChange = vi.fn()
    render(<PublicationEditor value="" onChange={handleChange} disabled={false} />)

    // Act
    await user.type(screen.getByRole('textbox'), 'x')

    // Assert
    expect(handleChange).toHaveBeenCalled()
  })
})
