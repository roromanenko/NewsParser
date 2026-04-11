import { useState } from 'react'

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

function ImageCell({ url }: { url: string }) {
  const [broken, setBroken] = useState(false)

  if (broken) {
    return (
      <div className="w-full h-full flex items-center justify-center">
        <span
          className="font-caps text-[10px] tracking-widest"
          style={{ color: 'var(--caramel)' }}
        >
          BROKEN
        </span>
      </div>
    )
  }

  return (
    <a href={url} target="_blank" rel="noopener noreferrer" className="w-full h-full block">
      <img
        src={url}
        alt=""
        loading="lazy"
        className="w-full h-full object-cover"
        onError={() => setBroken(true)}
      />
    </a>
  )
}

function VideoCell({ url }: { url: string }) {
  return (
    <video
      src={url}
      controls
      preload="metadata"
      className="w-full h-full object-cover"
    />
  )
}

export function MediaGallery({ items, title }: Props) {
  if (items.length === 0) return null

  return (
    <div
      className="border p-5"
      style={{
        background: 'rgba(61,15,15,0.4)',
        borderColor: 'rgba(255,255,255,0.1)',
      }}
    >
      <p
        className="font-caps text-[10px] tracking-widest mb-3"
        style={{ color: '#6b7280' }}
      >
        {title ?? 'MEDIA'}
      </p>
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
        {items.map(item => (
          <div
            key={item.id}
            className="aspect-video border overflow-hidden"
            style={{
              borderColor: 'rgba(255,255,255,0.1)',
              background: 'var(--near-black)',
            }}
          >
            {item.kind === 'Video' ? (
              <VideoCell url={item.url} />
            ) : (
              <ImageCell url={item.url} />
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
