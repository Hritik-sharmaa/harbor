import { useEffect, useRef, useState } from 'react'
import { AnimatePresence, LayoutGroup, motion, useInView } from 'motion/react'
import { still } from '../lib/motion'
import { liveActivities } from '../content'
import { Icon, SectionHead } from '../components/ui'
import './LiveActivities.css'

const glyphs = {
  charge: <Icon className="g-charge" d="M13 2 4 14h7l-1 8 9-12h-7z" />,
  download: <Icon className="g-download" d="M5 12l5 5 9-10" />,
  timer: <svg className="i g-timer" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" opacity=".25" /><circle className="ring" cx="12" cy="12" r="9" pathLength="1" /></svg>,
  meeting: <Icon className="g-meeting" d="M3 7h12v10H3zM15 10l6-3v10l-6-3" />,
}

const morph = { type: 'spring', stiffness: 300, damping: 28 }

export default function LiveActivities() {
  const ref = useRef()
  const seen = useInView(ref, { amount: .4 })
  const [i, setI] = useState(0)
  useEffect(() => {
    if (!seen || still()) return
    const t = setInterval(() => setI(n => (n + 1) % liveActivities.length), 2400)
    return () => clearInterval(t)
  }, [seen])
  const [kind, label, detail] = liveActivities[i]

  return (
    <section className="live">
      <div className="wrap live-grid">
        <div>
          <SectionHead align="left" eyebrow="Live activities" title="Quiet until *something happens.*">
            Closed, the island is a small pill under the menu bar. It wakes up for the things worth a glance, then gets out of the way.
          </SectionHead>
          <ol className="live-list fx">
            {liveActivities.map(([k, l, d], n) => (
              <li key={k}><button type="button" className={n === i ? 'on' : ''} onClick={() => setI(n)}>{glyphs[k]}<span>{l}</span><small>{d}</small></button></li>
            ))}
          </ol>
        </div>
        <div className="live-stage fx" ref={ref} aria-hidden="true">
          <div className="live-bar"><span>Harbor</span><span>10:34</span></div>
          <LayoutGroup>
            <motion.div layout className={`pill k-${kind}`} transition={morph} style={{ borderRadius: 22 }}>
              <AnimatePresence mode="popLayout" initial={false}>
                <motion.div
                  key={kind} className="pill-row" layout
                  initial={{ opacity: 0, filter: 'blur(6px)', scale: .9 }} animate={{ opacity: 1, filter: 'blur(0px)', scale: 1 }}
                  exit={{ opacity: 0, filter: 'blur(6px)', scale: .9 }} transition={{ duration: .3 }}
                >
                  <span className="lead">{glyphs[kind]}<b>{label}</b></span>
                  <span className="cam" />
                  <span className="trail">{detail}</span>
                </motion.div>
              </AnimatePresence>
            </motion.div>
          </LayoutGroup>
        </div>
      </div>
    </section>
  )
}
