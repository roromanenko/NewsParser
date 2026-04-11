import { useState, useEffect, useCallback } from 'react'
import { X, ChevronLeft, ChevronRight, Expand, ExternalLink, Play } from 'lucide-react'

export type MediaItem = {
  id: string
  url: string
  kind: 'Image' | 'Video'
  contentType: string
}

type Props = {
  items: MediaItem[]
  title?: string
}

// ---- Lightbox ----
function Lightbox({
  items,
  index,
  onClose,
  onPrev,
  onNext,
}: {
  items: MediaItem[]
  index: number
  onClose: () => void
  onPrev: () => void
  onNext: () => void
}) {
  const item = items[index]

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowLeft') onPrev()
      if (e.key === 'ArrowRight') onNext()
    }
    document.addEventListener('keydown', handler)
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', handler)
      document.body.style.overflow = prev
    }
  }, [onClose, onPrev, onNext])

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      style={{ background: 'rgba(0,0,0,0.92)', backdropFilter: 'blur(8px)' }}
      onClick={onClose}
    >
      {/* Close */}
      <button
        onClick={onClose}
        className="absolute top-4 right-4 p-2 transition-colors"
        style={{ color: '#6b7280' }}
        onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
        onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
      >
        <X className="w-6 h-6" />
      </button>

      {/* Counter */}
      <div
        className="absolute top-4 left-1/2 -translate-x-1/2 font-mono text-xs px-3 py-1"
        style={{ background: 'rgba(255,255,255,0.08)', color: '#9ca3af' }}
      >
        {index + 1} / {items.length}
      </div>

      {/* Prev */}
      {items.length > 1 && (
        <button
          className="absolute left-4 p-3 transition-colors"
          style={{ color: '#6b7280' }}
          onClick={e => { e.stopPropagation(); onPrev() }}
          onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
          onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
        >
          <ChevronLeft className="w-8 h-8" />
        </button>
      )}

      {/* Media */}
      <div
        className="relative max-w-5xl max-h-[85vh] mx-16 flex items-center justify-center"
        onClick={e => e.stopPropagation()}
      >
        {item.kind === 'Video' ? (
          <video
            src={item.url}
            controls
            autoPlay
            className="max-w-full max-h-[85vh]"
            style={{ outline: 'none' }}
          />
        ) : (
          <img
            src={item.url}
            alt=""
            className="max-w-full max-h-[85vh] object-contain"
            style={{ boxShadow: '0 0 60px rgba(0,0,0,0.6)' }}
          />
        )}

        {/* Open original */}
        <a
          href={item.url}
          target="_blank"
          rel="noopener noreferrer"
          className="absolute bottom-3 right-3 p-1.5 flex items-center gap-1 font-mono text-[10px] tracking-widest transition-colors"
          style={{ background: 'rgba(0,0,0,0.6)', color: '#6b7280' }}
          onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
          onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
        >
          <ExternalLink className="w-3 h-3" />
          ORIGINAL
        </a>
      </div>

      {/* Next */}
      {items.length > 1 && (
        <button
          className="absolute right-4 p-3 transition-colors"
          style={{ color: '#6b7280' }}
          onClick={e => { e.stopPropagation(); onNext() }}
          onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
          onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
        >
          <ChevronRight className="w-8 h-8" />
        </button>
      )}

      {/* Strip */}
      {items.length > 1 && (
        <div className="absolute bottom-4 left-1/2 -translate-x-1/2 flex gap-2">
          {items.map((_, i) => (
            <button
              key={i}
              onClick={e => { e.stopPropagation(); /* handled by parent index */ }}
              className="w-1.5 h-1.5 rounded-full transition-all"
              style={{
                background: i === index ? 'var(--caramel)' : 'rgba(255,255,255,0.25)',
                transform: i === index ? 'scale(1.4)' : 'scale(1)',
              }}
            />
          ))}
        </div>
      )}
    </div>
  )
}

// ---- Single grid cell ----
function MediaCell({
  item,
  onClick,
  featured = false,
}: {
  item: MediaItem
  onClick: () => void
  featured?: boolean
}) {
  const [broken, setBroken] = useState(false)
  const [hovered, setHovered] = useState(false)

  return (
    <div
      className="relative overflow-hidden cursor-pointer"
      style={{
        background: 'var(--near-black)',
        border: '1px solid rgba(255,255,255,0.08)',
        aspectRatio: featured ? '16/9' : '16/9',
      }}
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      {item.kind === 'Video' ? (
        <>
          <video
            src={item.url}
            preload="metadata"
            className="w-full h-full object-cover"
            muted
          />
          {/* Play overlay always visible */}
          <div
            className="absolute inset-0 flex items-center justify-center transition-colors"
            style={{ background: hovered ? 'rgba(0,0,0,0.5)' : 'rgba(0,0,0,0.3)' }}
          >
            <div
              className="rounded-full p-3 transition-transform"
              style={{
                background: 'rgba(255,255,255,0.1)',
                border: '1px solid rgba(255,255,255,0.2)',
                transform: hovered ? 'scale(1.1)' : 'scale(1)',
              }}
            >
              <Play className="w-5 h-5 fill-current" style={{ color: '#E8E8E8' }} />
            </div>
          </div>
        </>
      ) : broken ? (
        <div className="w-full h-full flex items-center justify-center">
          <span className="font-caps text-[10px] tracking-widest" style={{ color: '#4b5563' }}>
            UNAVAILABLE
          </span>
        </div>
      ) : (
        <img
          src={item.url}
          alt=""
          loading="lazy"
          className="w-full h-full object-cover transition-transform duration-300"
          style={{ transform: hovered ? 'scale(1.04)' : 'scale(1)' }}
          onError={() => setBroken(true)}
        />
      )}

      {/* Hover overlay */}
      {item.kind !== 'Video' && (
        <div
          className="absolute inset-0 flex items-center justify-center transition-opacity duration-200"
          style={{
            background: 'rgba(0,0,0,0.45)',
            opacity: hovered ? 1 : 0,
          }}
        >
          <Expand className="w-5 h-5" style={{ color: '#E8E8E8' }} />
        </div>
      )}

      {/* Kind badge */}
      <div
        className="absolute top-2 left-2 font-caps text-[9px] tracking-widest px-1.5 py-0.5"
        style={{
          background: 'rgba(0,0,0,0.7)',
          color: item.kind === 'Video' ? 'var(--caramel)' : '#9ca3af',
          opacity: hovered ? 1 : 0,
          transition: 'opacity 0.2s',
        }}
      >
        {item.kind.toUpperCase()}
      </div>
    </div>
  )
}

// ---- Gallery grid layouts ----
function GalleryGrid({
  items,
  onOpen,
}: {
  items: MediaItem[]
  onOpen: (i: number) => void
}) {
  const count = items.length

  if (count === 1) {
    return (
      <div className="max-w-xl">
        <MediaCell item={items[0]} featured onClick={() => onOpen(0)} />
      </div>
    )
  }

  if (count === 2) {
    return (
      <div className="grid grid-cols-2 gap-2">
        {items.map((item, i) => (
          <MediaCell key={item.id} item={item} onClick={() => onOpen(i)} />
        ))}
      </div>
    )
  }

  if (count === 3) {
    return (
      <div className="grid grid-cols-2 gap-2">
        <div className="row-span-2">
          <MediaCell item={items[0]} featured onClick={() => onOpen(0)} />
        </div>
        {items.slice(1).map((item, i) => (
          <MediaCell key={item.id} item={item} onClick={() => onOpen(i + 1)} />
        ))}
      </div>
    )
  }

  // 4+: featured first, rest in a 3-col grid below
  const [featured, ...rest] = items
  const MAX_REST = 5
  const visible = rest.slice(0, MAX_REST)
  const overflow = rest.length - MAX_REST

  return (
    <div className="space-y-2">
      {/* Featured */}
      <div className="max-w-full">
        <MediaCell item={featured} featured onClick={() => onOpen(0)} />
      </div>
      {/* Rest */}
      <div className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-2">
        {visible.map((item, i) => (
          <div key={item.id} className="relative">
            <MediaCell item={item} onClick={() => onOpen(i + 1)} />
            {/* Overflow badge on last visible */}
            {i === MAX_REST - 1 && overflow > 0 && (
              <div
                className="absolute inset-0 flex items-center justify-center cursor-pointer"
                style={{ background: 'rgba(0,0,0,0.7)' }}
                onClick={() => onOpen(i + 1)}
              >
                <span className="font-display text-2xl" style={{ color: '#E8E8E8' }}>
                  +{overflow + 1}
                </span>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

// ---- Public component ----
export function MediaGallery({ items, title }: Props) {
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null)

  const openAt = useCallback((i: number) => setLightboxIndex(i), [])
  const close = useCallback(() => setLightboxIndex(null), [])
  const prev = useCallback(
    () => setLightboxIndex(i => (i === null ? null : (i - 1 + items.length) % items.length)),
    [items.length],
  )
  const next = useCallback(
    () => setLightboxIndex(i => (i === null ? null : (i + 1) % items.length)),
    [items.length],
  )

  if (items.length === 0) return null

  return (
    <>
      <div
        className="border p-5"
        style={{
          background: 'rgba(61,15,15,0.4)',
          borderColor: 'rgba(255,255,255,0.1)',
        }}
      >
        <div className="flex items-center justify-between mb-4">
          <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
            {title ?? 'MEDIA'}
          </p>
          <span className="font-mono text-[10px]" style={{ color: '#4b5563' }}>
            {items.length} {items.length === 1 ? 'file' : 'files'}
          </span>
        </div>

        <GalleryGrid items={items} onOpen={openAt} />
      </div>

      {lightboxIndex !== null && (
        <Lightbox
          items={items}
          index={lightboxIndex}
          onClose={close}
          onPrev={prev}
          onNext={next}
        />
      )}
    </>
  )
}
