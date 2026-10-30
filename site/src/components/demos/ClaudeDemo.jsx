import { useEffect, useRef, useState } from 'react'
import { AnimatePresence, motion, useInView } from 'motion/react'
import { spring, still } from '../../lib/motion'
import { claudeAsks } from '../../content'
import { Icon } from '../ui'
import './ClaudeDemo.css'

// A permission request arrives, a cursor clicks Allow, the next one comes in
export default function ClaudeDemo() {
  const ref = useRef()
  const seen = useInView(ref, { amount: .4 })
  const [step, setStep] = useState(0)
  const [n, setN] = useState(0)
  useEffect(() => {
    if (!seen || still()) return
    const t = setTimeout(() => {
      if (step === 2) { setStep(0); setN(x => (x + 1) % claudeAsks.length) } else setStep(step + 1)
    }, step === 0 ? 1600 : 1200)
    return () => clearTimeout(t)
  }, [seen, step])
  return (
    <div className="demo claude-demo" ref={ref} aria-hidden="true">
      <div className="perm">
        <div className="perm-top"><span className="dot-live" /> Claude Code <span className="dim">· harbor</span></div>
        <AnimatePresence mode="wait">
          {step < 2 ? (
            <motion.div key={`a${n}`} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -8 }} transition={{ duration: .25 }}>
              <p className="perm-q">Wants to run</p>
              <code>{claudeAsks[n]}</code>
              <div className="perm-btns">
                <span>Deny</span><span>Always allow</span>
                <motion.span className="allow" animate={step === 1 ? { scale: [1, .92, 1] } : {}} transition={{ duration: .35 }}>Allow</motion.span>
              </div>
            </motion.div>
          ) : (
            <motion.div key={`ok${n}`} className="perm-ok" initial={{ opacity: 0, scale: .9 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0 }} transition={spring}>
              <span className="tick"><Icon d="M5 12l5 5 9-10" /></span> Allowed
            </motion.div>
          )}
        </AnimatePresence>
        <motion.span className="cursor" animate={step >= 1 ? { x: 0, y: 0, opacity: 1 } : { x: -70, y: 40, opacity: 0 }} transition={{ type: 'spring', stiffness: 120, damping: 18 }}>
          <svg viewBox="0 0 24 24"><path d="M5 3l14 8-6 2-3 6z" /></svg>
        </motion.span>
      </div>
    </div>
  )
}
