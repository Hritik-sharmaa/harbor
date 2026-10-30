import { Fragment } from 'react'
import { motion } from 'motion/react'
import { spring } from '../lib/motion'

export const Icon = ({ d, className = '' }) => (
  <svg className={`i ${className}`} viewBox="0 0 24 24" aria-hidden="true"><path d={d} /></svg>
)

export const GitHubMark = () => (
  <svg className="gh" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 .3a12 12 0 0 0-3.8 23.38c.6.12.83-.26.83-.57L9 21.07c-3.34.72-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.08-.74.09-.73.09-.73 1.2.09 1.83 1.24 1.83 1.24 1.08 1.83 2.81 1.3 3.5 1 .1-.78.42-1.31.76-1.61-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.14-.3-.54-1.52.1-3.18 0 0 1-.32 3.3 1.23a11.5 11.5 0 0 1 6 0c2.28-1.55 3.29-1.23 3.29-1.23.64 1.66.24 2.88.12 3.18a4.65 4.65 0 0 1 1.23 3.22c0 4.61-2.8 5.63-5.48 5.92.42.36.81 1.1.81 2.22l-.01 3.29c0 .31.2.69.82.57A12 12 0 0 0 12 .3" /></svg>
)

// Splits a headline into words GSAP can lift one by one. Words between *stars* are set in the italic accent.
export function Words({ text, as: Tag = 'h2', className = '' }) {
  let accent = false
  return (
    <Tag className={`split ${className}`} aria-label={text.replaceAll('*', '')}>
      {text.split(' ').map((raw, i) => {
        const opens = raw.startsWith('*'), closes = raw.endsWith('*')
        if (opens) accent = true
        const word = raw.replaceAll('*', '')
        const inner = accent ? <em>{word}</em> : word
        if (closes) accent = false
        return <Fragment key={i}>{i > 0 && ' '}<span className="w" aria-hidden="true"><span>{inner}</span></span></Fragment>
      })}
    </Tag>
  )
}

export const SectionHead = ({ eyebrow, title, children, align = 'center' }) => (
  <div className={`head ${align}`}>
    {eyebrow && <p className="eyebrow fx">{eyebrow}</p>}
    <Words text={title} />
    {children && <p className="sub fx">{children}</p>}
  </div>
)

// a card whose rim and glow follow the pointer
export function Card({ as: Tag = 'article', className = '', children }) {
  const move = e => {
    const r = e.currentTarget.getBoundingClientRect()
    e.currentTarget.style.setProperty('--x', `${e.clientX - r.left}px`)
    e.currentTarget.style.setProperty('--y', `${e.clientY - r.top}px`)
  }
  return <Tag className={`card ${className}`} onPointerMove={move}>{children}</Tag>
}

// Pill switcher with a highlight that slides between options
export function Segmented({ options, value, onChange, label, id, className = '' }) {
  return (
    <div className={`seg ${className}`} role="group" aria-label={label}>
      {options.map((name, n) => (
        <button key={name} type="button" aria-pressed={n === value} onClick={() => onChange(n)}>
          {n === value && <motion.span layoutId={`seg-${id}`} className="seg-hl" transition={spring} />}
          <span>{name}</span>
        </button>
      ))}
    </div>
  )
}
