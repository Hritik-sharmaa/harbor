import { useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { faq, GH } from '../content'
import { SectionHead, Segmented } from '../components/ui'
import './Faq.css'

const topics = Object.keys(faq)

export default function Faq() {
  const [t, setT] = useState(0)
  return (
    <section id="faq">
      <div className="wrap faq">
        <SectionHead eyebrow="Q&A" title="Questions, *answered.*">
          Pick a topic. Anything else, <a href={`${GH}/issues`}>ask on GitHub</a>.
        </SectionHead>
        <Segmented className="faq-topics fx" id="faq" label="Topics" options={topics} value={t} onChange={setT} />
        <AnimatePresence mode="wait">
          <motion.div key={t} className="faq-list" initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -12 }} transition={{ duration: .25 }}>
            {faq[topics[t]].map(([q, a], n) => (
              <details key={q} open={n === 0}>
                <summary>{q}<span className="plus" aria-hidden="true" /></summary>
                <p>{a}</p>
              </details>
            ))}
          </motion.div>
        </AnimatePresence>
      </div>
    </section>
  )
}
