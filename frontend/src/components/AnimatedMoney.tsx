import { useEffect, useState } from 'react'
import { money } from '../lib/format'

export default function AnimatedMoney({ value }: { value: number }) {
  const [display, setDisplay] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches ? value : 0)
  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { setDisplay(value); return }
    let frame = 0
    const start = performance.now()
    const tick = (now: number) => {
      const progress = Math.min(1, (now - start) / 750)
      setDisplay(value * (1 - Math.pow(1 - progress, 3)))
      if (progress < 1) frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [value])
  return <span className="tabular-nums"><span className="sr-only">{money(value)}</span><span aria-hidden="true">{money(display)}</span></span>
}
