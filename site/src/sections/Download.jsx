import { useRef } from 'react'
import { calm, gsap, useGSAP } from '../lib/motion'
import { DL, GH, installSteps } from '../content'
import { Words } from '../components/ui'
import './Download.css'

export default function Download() {
  const ref = useRef()
  useGSAP(() => {
    gsap.matchMedia().add(calm, () => {
      gsap.to('.cta-icon', { y: -14, rotate: 4, duration: 2.6, ease: 'sine.inOut', yoyo: true, repeat: -1 })
      gsap.fromTo('.cta-rays', { rotate: -8 }, { rotate: 8, ease: 'none', scrollTrigger: { trigger: ref.current, start: 'top bottom', end: 'bottom top', scrub: true } })
    })
  }, { scope: ref })
  return (
    <section className="get" id="get" ref={ref}>
      <div className="wrap">
        <div className="cta-box fx">
          <div className="cta-rays" aria-hidden="true" />
          <img className="cta-icon" src="icon.png" width="112" height="112" alt="" />
          <Words text="Install it in *under a minute.*" />
          <p className="sub">Harbor installs for your user only and puts the Windows taskbar back the moment you quit.</p>
          <ol className="install">
            {installSteps.map((s, n) => <li key={s}><span>{n + 1}</span> {s}</li>)}
          </ol>
          <div className="cta-btns">
            <a className="btn" href={DL}>Download Harbor</a>
            <a className="btn ghost" href={`${GH}#build-from-source`}>Build from source</a>
          </div>
          <p className="fine">Windows 10 (1903 or later) or Windows 11. Free and open source.</p>
        </div>
      </div>
    </section>
  )
}
