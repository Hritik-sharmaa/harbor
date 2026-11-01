import { useEffect, useRef, useState } from 'react'
import { AnimatePresence, motion, useInView } from 'motion/react'
import { still } from '../lib/motion'
import { liveActivities } from '../content'
import { Icon, SectionHead } from '../components/ui'
import './LiveActivities.css'

const glyphs = {
  charge: <Icon className="g-charge" d="M13 2 4 14h7l-1 8 9-12h-7z" />,
  headphones: <Icon d="M4 15v-3a8 8 0 0 1 16 0v3M4 15h3v5H4zM17 15h3v5h-3z" />,
  download: <Icon d="M12 4v11m-5-4 5 5 5-5M5 20h14" />,
  timer: <Icon d="M12 8v5l3 2M9 2h6M12 5a8 8 0 1 0 0 16 8 8 0 0 0 0-16z" />,
  meeting: <Icon d="M3 7h12v10H3zM15 10l6-3v10l-6-3" />,
}
const tints = { charge: '#30d158', headphones: '#64b4ff', download: '#3b82f6', timer: '#ffb340', meeting: '#ff9f0a' }

// the same spring as the pill in Harbor: out wide and short, then down with a small bounce
const grow = { width: { type: 'spring', stiffness: 260, damping: 18 }, height: { type: 'spring', stiffness: 220, damping: 15, delay: .06 } }

function Ring({ level, tint }) {
  return (
    <svg className="ring" viewBox="0 0 40 40" aria-hidden="true">
      <circle cx="20" cy="20" r="17" stroke={tint} opacity=".25" />
      <motion.circle
        cx="20" cy="20" r="17" stroke={tint} pathLength="1" strokeDasharray="1 1"
        initial={{ strokeDashoffset: 1 }} animate={{ strokeDashoffset: 1 - level }} transition={{ duration: .8, delay: .35, ease: [0.16, 1, 0.3, 1] }}
      />
    </svg>
  )
}

export default function LiveActivities() {
  const ref = useRef()
  const seen = useInView(ref, { amount: .4 })
  const [i, setI] = useState(0)
  const [open, setOpen] = useState(true)
  // each pill folds back into the menu bar before the next one springs out
  useEffect(() => {
    if (!seen || still()) return
    let t = setTimeout(function next() {
      setOpen(false)
      t = setTimeout(() => { setI(n => (n + 1) % liveActivities.length); setOpen(true); t = setTimeout(next, 2600) }, 420)
    }, 2600)
    return () => clearTimeout(t)
  }, [seen])
  const pick = n => { setI(n); setOpen(true) }
  const [kind, title, detail, right] = liveActivities[i]
  const tint = tints[kind]

  return (
    <section className="live">
      <div className="wrap live-grid">
        <div>
          <SectionHead align="left" eyebrow="Live activities" title="Quiet until *something happens.*">
            A small black pill springs out of the menu bar for the things worth a glance, then folds back out of the way.
          </SectionHead>
          <ol className="live-list fx">
            {liveActivities.map(([k, t, d], n) => (
              <li key={k}><button type="button" className={n === i ? 'on' : ''} onClick={() => pick(n)}>{glyphs[k]}<span>{t}</span><small>{d}</small></button></li>
            ))}
          </ol>
        </div>
        <div className="live-stage fx" ref={ref} aria-hidden="true">
          <div className="live-bar"><span>Harbor</span><span>10:34</span></div>
          <motion.div
            className="pill" style={{ '--tint': tint }}
            initial={false}
            animate={open ? { width: 360, height: 64 } : { width: 130, height: 8 }}
            transition={grow}
          >
            <span className="ear l" /><span className="ear r" />
            <AnimatePresence mode="wait">
              {open && (
                <motion.div
                  key={kind} className="pill-row"
                  initial={{ opacity: 0, y: -6, filter: 'blur(6px)' }}
                  animate={{ opacity: 1, y: 0, filter: 'blur(0px)', transition: { delay: .2, duration: .3 } }}
                  exit={{ opacity: 0, transition: { duration: .1 } }}
                >
                  <span className="lead">
                    {right.level != null ? <Ring level={right.level} tint={tint} /> : <span className="dot" />}
                    {glyphs[kind]}
                  </span>
                  <span className="text"><b>{title}</b><small>{detail}</small></span>
                  {right.big && <span className="big">{right.big}</span>}
                  {right.icon && <span className="pulse">{glyphs.headphones}</span>}
                  {right.action && (
                    <motion.span className="act" initial={{ scale: .6, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} transition={{ type: 'spring', stiffness: 420, damping: 14, delay: .35 }}>
                      {right.action}
                    </motion.span>
                  )}
                </motion.div>
              )}
            </AnimatePresence>
          </motion.div>
        </div>
      </div>
    </section>
  )
}
