export const MS_PER_DAY = 86_400_000

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

export function daysAgoIso(days: number): string {
  return new Date(Date.now() - days * MS_PER_DAY).toISOString().slice(0, 10)
}
