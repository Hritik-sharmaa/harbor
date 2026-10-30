import { useEffect, useState } from 'react'
import { still } from '../../lib/motion'
import './PerfDemo.css'

// A made-up but representative trace: near-idle CPU with the odd blip, memory wandering inside 24–36 MB
export default function PerfDemo() {
  const [pts, setPts] = useState(() => Array.from({ length: 24 }, () => 1 + Math.random() * 4))
  const [mb, setMb] = useState(28)
  useEffect(() => {
    if (still()) return
    const t = setInterval(() => {
      setPts(p => [...p.slice(1), Math.random() < .12 ? 6 + Math.random() * 8 : 1 + Math.random() * 3])
      setMb(m => Math.max(24, Math.min(36, m + Math.round((Math.random() - .5) * 3))))
    }, 900)
    return () => clearInterval(t)
  }, [])
  const path = pts.map((v, n) => `${n ? 'L' : 'M'}${(n / (pts.length - 1)) * 300} ${80 - v * 4}`).join(' ')
  return (
    <div className="demo perf-demo" aria-hidden="true">
      <div className="readouts">
        <div><small>Memory</small><b>{mb}<em> MB</em></b></div>
        <div><small>CPU</small><b>{pts[pts.length - 1].toFixed(1)}<em> %</em></b></div>
      </div>
      <svg viewBox="0 0 300 80" preserveAspectRatio="none">
        <defs><linearGradient id="pf" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor="#30d158" stopOpacity=".35" /><stop offset="1" stopColor="#30d158" stopOpacity="0" /></linearGradient></defs>
        <path className="area" d={`${path} L300 80 L0 80 Z`} />
        <path className="line" d={path} />
      </svg>
    </div>
  )
}
