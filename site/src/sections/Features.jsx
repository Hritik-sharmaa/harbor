import { shot, smallThings } from '../content'
import { Card, SectionHead } from '../components/ui'
import ClaudeDemo from '../components/demos/ClaudeDemo'
import PerfDemo from '../components/demos/PerfDemo'
import PrivacyDemo from '../components/demos/PrivacyDemo'
import './Features.css'

const tiles = [
  ['b-dock', <div className="demo dock-demo" aria-hidden="true"><img src={shot('dock')} width="840" height="140" alt="" /></div>,
    'A dock that feels right', 'Magnifies, bounces, shows stacks and badges, and hides only when a window covers it.'],
  ['b-perf', <PerfDemo />, 'Light enough to forget', 'Plain Win32 and GDI+. No browser engine inside, close to 0% CPU when idle.'],
  ['b-claude', <ClaudeDemo />, 'Claude Code approvals', 'Allow, Always allow or Deny from the island, whatever editor you use.'],
  ['b-privacy', <PrivacyDemo />, 'Private by default', 'No telemetry, no analytics, no account. Everything stays on your PC.'],
]

export default function Features() {
  return (
    <section id="features">
      <div className="wrap">
        <SectionHead eyebrow="Features" title="Everything Windows *forgot to ship.*">
          The parts of macOS you miss, rebuilt natively for Windows. Each one is live below.
        </SectionHead>
        <div className="bento">
          {tiles.map(([cls, demo, title, text]) => (
            <Card key={cls} className={`${cls} fx`}>
              {demo}
              <div className="card-text"><h3>{title}</h3><p>{text}</p></div>
            </Card>
          ))}
        </div>
        <dl className="facts">
          {smallThings.map(([title, text]) => <div className="fx" key={title}><dt>{title}</dt><dd>{text}</dd></div>)}
        </dl>
      </div>
    </section>
  )
}
