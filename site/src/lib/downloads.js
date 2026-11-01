import { useEffect, useState } from 'react'
import { animate } from 'motion/react'
import { still } from './motion'

const API = 'https://api.github.com/repos/hritik-sharmaa/harbor/releases?per_page=100'
const KEY = 'harbor-downloads'

// Every download of every release asset, straight from GitHub's public release stats.
// Kept for ten minutes per visitor so reloading doesn't eat into GitHub's hourly limit.
async function total() {
  try {
    const hit = JSON.parse(sessionStorage.getItem(KEY) || 'null')
    if (hit && Date.now() - hit.at < 10 * 60 * 1000) return hit.n
  } catch { /* storage blocked: just ask GitHub */ }
  const res = await fetch(API, { headers: { Accept: 'application/vnd.github+json' } })
  if (!res.ok) throw new Error(`GitHub ${res.status}`)
  const n = (await res.json()).reduce((sum, r) => sum + r.assets.reduce((s, a) => s + a.download_count, 0), 0)
  try { sessionStorage.setItem(KEY, JSON.stringify({ n, at: Date.now() })) } catch { /* fine */ }
  return n
}

/** The total download count (null until it arrives, or if GitHub can't be reached). */
export function useDownloads() {
  const [n, setN] = useState(null)
  useEffect(() => {
    let live = true
    total().then(v => live && setN(v)).catch(() => {})
    return () => { live = false }
  }, [])
  return n
}

/** Counts up to the number once it's known (straight to it when motion is reduced). */
export function useCountUp(target) {
  const [shown, setShown] = useState(0)
  useEffect(() => {
    if (target == null) return
    if (still()) { setShown(target); return }
    const a = animate(0, target, { duration: 1.6, ease: [0.16, 1, 0.3, 1], onUpdate: v => setShown(Math.round(v)) })
    return () => a.stop()
  }, [target])
  return shown
}
