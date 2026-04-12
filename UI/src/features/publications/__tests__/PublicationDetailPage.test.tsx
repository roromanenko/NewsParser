import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { PublicationDetailPage } from '../PublicationDetailPage'
import type { PublicationDetailDto } from '../types'

// Mock hooks that reach the network
vi.mock('../usePublicationDetail', () => ({
  usePublicationDetail: vi.fn(),
}))

vi.mock('../usePublicationMutations', () => ({
  usePublicationMutations: vi.fn(),
}))

// requestAnimationFrame stub (needed by PublicationEditor)
beforeEach(() => {
  vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
    cb(0)
    return 0
  })
})

import { usePublicationDetail } from '../usePublicationDetail'
import { usePublicationMutations } from '../usePublicationMutations'

// Minimal stub — only the fields used by PublicationDetailPage
const mockMutations = {
  generateContent: { mutate: vi.fn(), isPending: false },
  updateContent: { mutate: vi.fn(), isPending: false },
  approve: { mutate: vi.fn(), isPending: false },
  reject: { mutate: vi.fn(), isPending: false },
}

function buildPublication(overrides: Partial<PublicationDetailDto> = {}): PublicationDetailDto {
  return {
    id: 'pub-1',
    status: 'ContentReady',
    targetName: 'My Channel',
    platform: 'Telegram',
    eventTitle: null,
    generatedContent: 'Generated text',
    availableMedia: [],
    selectedMediaFileIds: [],
    createdAt: '2024-01-15T10:00:00Z',
    approvedAt: null,
    publishedAt: null,
    rejectionReason: null,
    ...overrides,
  }
}

function renderPage(publicationId = 'pub-1') {
  return render(
    <MemoryRouter initialEntries={[`/publications/${publicationId}`]}>
      <Routes>
        <Route path="/publications/:id" element={<PublicationDetailPage />} />
      </Routes>
    </MemoryRouter>
  )
}

beforeEach(() => {
  // Cast through unknown to avoid having to satisfy the full UseMutationResult shape
  vi.mocked(usePublicationMutations).mockReturnValue(
    mockMutations as unknown as ReturnType<typeof usePublicationMutations>
  )
})

describe('PublicationDetailPage — loading and not found states', () => {
  it('shows a loading indicator while data is loading', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({ publication: undefined, isLoading: true, error: null })

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('shows a not-found message when publication is null and not loading', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({ publication: undefined, isLoading: false, error: null })

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Publication not found.')).toBeInTheDocument()
  })
})

describe('PublicationDetailPage — back button', () => {
  it('renders the back button regardless of publication status', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Published' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.getByRole('button', { name: /back to publications/i })).toBeInTheDocument()
  })
})

describe('PublicationDetailPage — no Send button', () => {
  it('does not render a Send button for ContentReady status', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'ContentReady' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    const allButtons = screen.getAllByRole('button').map(b => b.textContent)
    expect(allButtons.every(t => !/send/i.test(t ?? ''))).toBe(true)
  })

  it('does not render a Send button for Approved status', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Approved' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    const allButtons = screen.getAllByRole('button').map(b => b.textContent)
    expect(allButtons.every(t => !/send/i.test(t ?? ''))).toBe(true)
  })
})

describe('PublicationDetailPage — Approve/Reject button visibility', () => {
  it('shows both Approve and Reject buttons when status is ContentReady', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'ContentReady' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.getByRole('button', { name: 'APPROVE' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'REJECT' })).toBeInTheDocument()
  })

  it('hides the Approve button when status is Approved', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Approved' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.queryByRole('button', { name: 'APPROVE' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'REJECT' })).toBeInTheDocument()
  })

  it('hides both Approve and Reject buttons when status is Published', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Published' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.queryByRole('button', { name: 'APPROVE' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'REJECT' })).not.toBeInTheDocument()
  })

  it('hides both Approve and Reject buttons when status is Rejected', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Rejected' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.queryByRole('button', { name: 'APPROVE' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'REJECT' })).not.toBeInTheDocument()
  })
})

describe('PublicationDetailPage — Approved status content area', () => {
  it('shows "Awaiting publication by worker." message when status is Approved', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'Approved' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Awaiting publication by worker.')).toBeInTheDocument()
  })

  it('does not show "Awaiting publication" message when status is ContentReady', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ status: 'ContentReady' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.queryByText('Awaiting publication by worker.')).not.toBeInTheDocument()
  })
})

describe('PublicationDetailPage — title display', () => {
  it('uses eventTitle as the page heading when eventTitle is present', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ eventTitle: 'Event Headline', targetName: 'Channel' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.getByRole('heading', { name: 'Event Headline' })).toBeInTheDocument()
  })

  it('falls back to targetName as the heading when eventTitle is null', () => {
    // Arrange
    vi.mocked(usePublicationDetail).mockReturnValue({
      publication: buildPublication({ eventTitle: null, targetName: 'My Channel' }),
      isLoading: false,
      error: null,
    })

    // Act
    renderPage()

    // Assert
    expect(screen.getByRole('heading', { name: 'My Channel' })).toBeInTheDocument()
  })
})
