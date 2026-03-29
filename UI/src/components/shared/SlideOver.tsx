import React, { useEffect } from 'react'
import ReactDOM from 'react-dom'
import { X } from 'lucide-react'

interface SlideOverProps {
  isOpen: boolean
  onClose: () => void
  title: string
  children: React.ReactNode
}

export function SlideOver({ isOpen, onClose, title, children }: SlideOverProps) {
  useEffect(() => {
    if (!isOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [isOpen, onClose])

  if (!isOpen) return null

  return ReactDOM.createPortal(
    <>
      <div className="fixed inset-0 z-40 bg-black/60" onClick={onClose} />
      <div
        className="fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col shadow-xl"
        style={{ background: 'var(--burgundy)', borderLeft: '1px solid rgba(255,255,255,0.1)' }}
      >
        <div
          className="flex items-center justify-between px-6 py-5 border-b"
          style={{ borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <h2 className="font-caps text-sm tracking-widest" style={{ color: 'var(--caramel)' }}>
            {title}
          </h2>
          <button
            onClick={onClose}
            className="transition-colors"
            style={{ color: '#6b7280' }}
            onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
            onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
          >
            <X className="w-5 h-5" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto">{children}</div>
      </div>
    </>,
    document.body
  )
}
