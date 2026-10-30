import { useEffect, useRef, useState } from 'react'
import { AnimatePresence, motion, useInView } from 'motion/react'
import { calm, gsap, still, useGSAP } from '../lib/motion'
import { CHANGELOG, DL, GH, heroSpecs, islandViews, shot } from '../content'
import { GitHubMark, Segmented, Words } from '../components/ui'
import './Hero.css'

// The island on a real Harbor desktop. It cycles through its views on its own until
// someone picks a tag, then it stays where they put it.
function useIslandTour(ref) {
  const seen = useInView(ref, { amount: .35 })
  const [view, setView] = useState(0)
  const [open, setOpen] = useState(false)
  const [held, setHeld] = useState(false)
  useEffect(() => {
    if (!seen) return
    if (held || still()) { setOpen(true); return }
    let t
    const cycle = () => {
      setOpen(true)
      t = setTimeout(() => {
        setOpen(false)
        t = setTimeout(() => { setView(v => (v + 1) % islandViews.length); cycle() }, 650)
      }, 3600)
    }
    t = setTimeout(cycle, 900)
    return () => clearTimeout(t)
  }, [seen, held])
  const pick = n => { setHeld(true); setView(n); setOpen(true) }
  return { view, open, pick }
}

export default function Hero() {
  const ref = useRef()
  const stage = useRef()
  const { view, open, pick } = useIslandTour(stage)
  const [id, name, caption] = islandViews[view]

  useGSAP(() => {
    gsap.matchMedia().add(calm, () => {
      gsap.timeline({ delay: .5, defaults: { ease: 'expo.out' } })
        .from('.pill-link', { y: 20, opacity: 0, duration: 1 })
        .from('h1 .w > span', { yPercent: 110, filter: 'blur(12px)', duration: 1.4, stagger: .07 }, '-=.7')
        .from('.hero-lead, .hero-cta, .hero-specs', { y: 24, opacity: 0, filter: 'blur(6px)', duration: 1.2, stagger: .1 }, '-=1.1')
        .from('.desk-wrap', { y: 140, opacity: 0, duration: 1.8 }, '-=1')
        .from('.hero-tags, .hero-caption', { y: 20, opacity: 0, duration: 1, stagger: .1 }, '-=1.2')
      gsap.fromTo('.desk', { rotateX: 26, scale: .86 }, {
        rotateX: 0, scale: 1, ease: 'none',
        scrollTrigger: { trigger: '.desk-wrap', start: 'top 90%', end: 'top 15%', scrub: 1 },
      })
      gsap.to('.desk-glow', { opacity: 1, scale: 1.2, ease: 'none', scrollTrigger: { trigger: '.desk-wrap', start: 'top 80%', end: 'center center', scrub: true } })
    })
  }, { scope: ref })

  return (
    <section className="hero" id="top" ref={ref}>
      <div className="hero-sky" aria-hidden="true" />
      <div className="wrap hero-copy">
        <a className="pill-link" href={CHANGELOG}><span className="pill-new">0.3.0</span> A notch for everyone →</a>
        <Words as="h1" text="Windows, with a *Mac's manners.*" />
        <p className="hero-lead">
          A dock, a menu bar, Control Center and a drop-down island for Windows 10 and 11.
          Native code that stays light and out of your way.
        </p>
        <div className="hero-cta">
          <a className="btn" href={DL}>Download for Windows</a>
          <a className="btn ghost" href={GH}><GitHubMark /> Star on GitHub</a>
        </div>
        <ul className="hero-specs">{heroSpecs.map(s => <li key={s}>{s}</li>)}</ul>
      </div>

      <div className="wrap desk-wrap" ref={stage}>
        <div className="desk-glow" aria-hidden="true" />
        <div className="desk">
          <img className="desk-shot" src={shot('hero')} width="1920" height="1080" alt="Harbor on a Windows desktop: the menu bar, Control Center and the dock" />
          <motion.div
            className="island" aria-hidden="true"
            animate={open ? { width: '35.4%', height: '26.3%' } : { width: '8%', height: '2.4%' }}
            transition={{ type: 'spring', stiffness: 230, damping: 26 }}
          >
            <AnimatePresence mode="wait">
              {open && (
                <motion.img
                  key={id} src={shot(`island_${id}`)} alt=""
                  initial={{ opacity: 0, filter: 'blur(8px)', scale: .96 }}
                  animate={{ opacity: 1, filter: 'blur(0px)', scale: 1, transition: { delay: .18, duration: .35 } }}
                  exit={{ opacity: 0, transition: { duration: .12 } }}
                />
              )}
            </AnimatePresence>
          </motion.div>
        </div>
      </div>

      <div className="wrap hero-under">
        <Segmented className="hero-tags" id="island" label="Island views" options={islandViews.map(v => v[1])} value={view} onChange={pick} />
        <div className="hero-caption" aria-live="polite">
          <AnimatePresence mode="wait">
            <motion.div key={id} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -8 }} transition={{ duration: .25 }}>
              <img className="caption-shot" src={shot(`island_${id}`)} width="680" height="284" alt={`The island showing ${name}`} />
              <p><b>{name}.</b> {caption}</p>
            </motion.div>
          </AnimatePresence>
        </div>
      </div>
    </section>
  )
}
