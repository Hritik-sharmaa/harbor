import { useRef } from 'react'
import { calm, gsap, useGSAP } from '../lib/motion'
import { panels, shot } from '../content'
import { SectionHead } from '../components/ui'
import './Panels.css'

// On wide screens the section pins and vertical scroll slides the panels sideways; elsewhere it's a swipeable row
export default function Panels() {
  const ref = useRef()
  useGSAP(() => {
    gsap.matchMedia().add(`(min-width: 900px) and ${calm}`, () => {
      const sec = ref.current
      const track = sec.querySelector('.track')
      sec.classList.add('pinned')
      const dist = () => track.scrollWidth - window.innerWidth
      const slide = gsap.to(track, {
        x: () => -dist(), ease: 'none',
        scrollTrigger: { trigger: sec, pin: true, scrub: 1, end: () => `+=${dist()}`, invalidateOnRefresh: true },
      })
      gsap.utils.toArray('.panel', sec).forEach(p => gsap.fromTo(p, { rotateY: -24, opacity: .35, scale: .9 }, {
        rotateY: 0, opacity: 1, scale: 1, ease: 'power2.out',
        scrollTrigger: { trigger: p, containerAnimation: slide, start: 'left 100%', end: 'left 50%', scrub: true },
      }))
      return () => sec.classList.remove('pinned')
    })
  }, { scope: ref })

  return (
    <section className="panels" id="panels" ref={ref}>
      <div className="wrap">
        <SectionHead eyebrow="Panels" title="Every panel, *one click away.*">
          They replace the Windows flyouts with something calmer. Shown at actual size.
        </SectionHead>
      </div>
      <div className="track" tabIndex={0} aria-label="Harbor panels at actual size">
        {panels.map(([n, w, h, title, text]) => (
          <figure className="panel" key={n}>
            <img src={shot(n)} width={w} height={h} style={{ '--h': `${h}px` }} alt={title} loading="lazy" />
            <figcaption><b>{title}</b>{text}</figcaption>
          </figure>
        ))}
      </div>
    </section>
  )
}
