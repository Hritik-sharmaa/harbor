import { useEffect, useState } from 'react'
import { motion } from 'motion/react'
import { DL, navLinks } from '../content'
import './Nav.css'

// The navigation is a notch: it hangs from the top edge, opens out from a closed island on load,
// and draws a shadow once the page scrolls under it.
export default function Nav() {
  const [down, setDown] = useState(false)
  useEffect(() => {
    const on = () => setDown(scrollY > 40)
    on(); addEventListener('scroll', on, { passive: true })
    return () => removeEventListener('scroll', on)
  }, [])
  return (
    <motion.header
      className={`notch-nav ${down ? 'down' : ''}`}
      initial={{ width: 132 }} animate={{ width: 'auto' }}
      transition={{ type: 'spring', stiffness: 170, damping: 24, delay: .35 }}
    >
      <motion.div className="notch-inner" initial={{ opacity: 0, filter: 'blur(6px)' }} animate={{ opacity: 1, filter: 'blur(0px)' }} transition={{ delay: .7, duration: .5 }}>
        <a className="brand" href="#top"><img src="icon.png" width="24" height="24" alt="" /> Harbor</a>
        <nav aria-label="Sections">
          {navLinks.map(([label, href]) => <a key={href} href={href}>{label}</a>)}
        </nav>
        <a className="btn sm" href={DL}>Download</a>
      </motion.div>
    </motion.header>
  )
}
