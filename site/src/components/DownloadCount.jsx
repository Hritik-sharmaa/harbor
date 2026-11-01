import { motion } from 'motion/react'
import { useCountUp, useDownloads } from '../lib/downloads'
import './DownloadCount.css'

// "26 downloads so far": read live from GitHub's release stats, counting up when it arrives
export default function DownloadCount({ className = '', suffix = 'downloads so far' }) {
  const n = useDownloads()
  const shown = useCountUp(n)
  if (n == null) return null
  return (
    <motion.span
      className={`dl-count ${className}`}
      initial={{ opacity: 0, y: 8, scale: .96 }} animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ type: 'spring', stiffness: 320, damping: 20 }}
    >
      <span className="dl-dot" aria-hidden="true" />
      <b>{shown.toLocaleString()}</b> {suffix}
    </motion.span>
  )
}
