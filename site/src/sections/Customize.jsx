import { useRef, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { calm, gsap, spring, useGSAP } from '../lib/motion'
import { settingsPages, shot } from '../content'
import { SectionHead } from '../components/ui'
import './Customize.css'

// A list of settings pages on the left; the real screenshot of the chosen one on the right
export default function Customize() {
  const [p, setP] = useState(0)
  const ref = useRef()
  useGSAP(() => {
    gsap.matchMedia().add(calm, () => {
      gsap.fromTo('.settings', { rotateY: -14, rotateX: 8, y: 60, scale: .94 }, {
        rotateY: 0, rotateX: 0, y: 0, scale: 1, ease: 'none',
        scrollTrigger: { trigger: '.settings', start: 'top bottom', end: 'top 30%', scrub: 1 },
      })
    })
  }, { scope: ref })
  const [tab, img] = settingsPages[p]

  return (
    <section id="customize" ref={ref}>
      <div className="wrap">
        <SectionHead eyebrow="Customize" title="Make it yours, *live.*">
          Sizes, colours, blur and corners update as you drag. Open Harbor from the Start menu whenever you want to change it.
        </SectionHead>
        <div className="cust">
          <ul className="cust-tabs" aria-label="Settings pages">
            {settingsPages.map(([name, , title, text], n) => (
              <li key={name}>
                <button type="button" aria-pressed={n === p} onClick={() => setP(n)} className="fx">
                  {n === p && <motion.span layoutId="cust-hl" className="cust-hl" transition={spring} />}
                  <span className="cust-name">{name}</span>
                  <strong>{title}</strong>
                  <span className="cust-text">{text}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="settings-wrap">
            <div className="settings-glow" aria-hidden="true" />
            <div className="settings">
              <AnimatePresence initial={false}>
                <motion.img
                  key={p} src={shot(img)} width="820" height="640" alt={`Harbor settings: ${tab}`}
                  initial={{ opacity: 0, scale: 1.03, filter: 'blur(6px)' }} animate={{ opacity: 1, scale: 1, filter: 'blur(0px)' }} exit={{ opacity: 0 }} transition={{ duration: .4 }}
                />
              </AnimatePresence>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
