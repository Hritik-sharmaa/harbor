import { useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
import { useGSAP } from '@gsap/react'
import Lenis from 'lenis'
import 'lenis/dist/lenis.css'

gsap.registerPlugin(ScrollTrigger, useGSAP)

export { gsap, ScrollTrigger, useGSAP }
export const calm = '(prefers-reduced-motion: no-preference)'
export const still = () => matchMedia('(prefers-reduced-motion: reduce)').matches
export const spring = { type: 'spring', stiffness: 380, damping: 34 }

// Lenis smooth scroll, driven by GSAP's ticker so ScrollTrigger stays in sync
export function useSmoothScroll() {
  useEffect(() => {
    if (still()) return
    const lenis = new Lenis({ anchors: { offset: -20 }, lerp: .1 })
    lenis.on('scroll', ScrollTrigger.update)
    const raf = t => lenis.raf(t * 1000)
    gsap.ticker.add(raf)
    gsap.ticker.lagSmoothing(0)
    return () => { gsap.ticker.remove(raf); lenis.destroy() }
  }, [])
}

// Page-wide entrances: split headings rise word by word, anything marked .fx fades up in batches
export function useReveal(scope) {
  useGSAP(() => {
    gsap.matchMedia().add(calm, () => {
      gsap.utils.toArray('section:not(.hero) .split').forEach(h => gsap.from(h.querySelectorAll('.w > span'), {
        yPercent: 110, filter: 'blur(10px)', duration: 1.2, ease: 'expo.out', stagger: .06,
        scrollTrigger: { trigger: h, start: 'top 85%' },
      }))
      gsap.set('.fx', { opacity: 0, y: 50, filter: 'blur(8px)' })
      ScrollTrigger.batch('.fx', {
        start: 'top 90%',
        onEnter: els => gsap.to(els, { opacity: 1, y: 0, filter: 'blur(0px)', duration: 1.1, ease: 'expo.out', stagger: .09 }),
      })
    })
  }, { scope })
}
