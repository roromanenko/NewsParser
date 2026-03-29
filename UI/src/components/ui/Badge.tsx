import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

const badgeVariants = cva(
  'inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium',
  {
    variants: {
      variant: {
        positive: 'bg-green-100 text-green-800',
        negative: 'bg-red-100 text-red-800',
        neutral: 'bg-gray-100 text-gray-600',
        info: 'bg-blue-100 text-blue-800',
        warning: 'bg-yellow-100 text-yellow-800',
        admin: 'bg-purple-100 text-purple-800',
      },
    },
    defaultVariants: {
      variant: 'neutral',
    },
  }
)

interface BadgeProps extends VariantProps<typeof badgeVariants> {
  children: React.ReactNode
  className?: string
}

export function Badge({ variant, children, className }: BadgeProps) {
  return <span className={cn(badgeVariants({ variant }), className)}>{children}</span>
}
