import { useRef } from 'react'
import { MotionConfig } from 'motion/react'
import { useReveal, useSmoothScroll } from './lib/motion'
import Nav from './components/Nav'
import Footer from './components/Footer'
import Hero from './sections/Hero'
import LiveActivities from './sections/LiveActivities'
import Features from './sections/Features'
import Panels from './sections/Panels'
import Customize from './sections/Customize'
import Faq from './sections/Faq'
import Download from './sections/Download'

export default function App() {
  const root = useRef()
  useSmoothScroll()
  useReveal(root)
  return (
    <MotionConfig reducedMotion="user">
      <div ref={root}>
        <Nav />
        <main>
          <Hero />
          <LiveActivities />
          <Features />
          <Panels />
          <Customize />
          <Faq />
          <Download />
        </main>
        <Footer />
      </div>
    </MotionConfig>
  )
}
