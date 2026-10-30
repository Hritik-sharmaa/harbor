import { useEffect, useRef, useState } from 'react'
import { useInView } from 'motion/react'
import { still } from '../../lib/motion'
import { connections } from '../../content'
import './PrivacyDemo.css'

// Lights up each connection Harbor can make in turn; telemetry stays struck out
export default function PrivacyDemo() {
  const ref = useRef()
  const seen = useInView(ref, { amount: .4 })
  const [hit, setHit] = useState(-1)
  useEffect(() => {
    if (!seen || still()) return
    const t = setInterval(() => setHit(h => (h + 1) % (connections.length + 1)), 1500)
    return () => clearInterval(t)
  }, [seen])
  return (
    <div className="demo net-demo" ref={ref} aria-hidden="true">
      <div className="net">
        <p className="net-head">Every connection Harbor makes</p>
        {connections.map(([host, why], n) => (
          <div className={`net-row ${hit === n ? 'hit' : ''}`} key={host}><code>{host}</code><span>{why}</span></div>
        ))}
        <div className="net-row off"><code>telemetry</code><span>none</span></div>
      </div>
    </div>
  )
}
