import { CHANGELOG, DL, GH } from '../content'
import './Footer.css'

export default function Footer() {
  return (
    <footer className="footer">
      <div className="wrap foot">
        <div>
          <a className="brand" href="#top"><img src="icon.png" width="24" height="24" alt="" /> Harbor</a>
          <p>Windows, with a Mac's manners.</p>
        </div>
        <nav aria-label="Footer">
          <a href={DL}>Download</a>
          <a href={GH}>GitHub</a>
          <a href={CHANGELOG}>Changelog</a>
          <a href={`${GH}#privacy`}>Privacy</a>
          <a href={`${GH}/issues`}>Report an issue</a>
        </nav>
      </div>
      <div className="wrap legal">
        <span>© 2026 Hritik Sharma. MIT License.</span>
        <span>Not affiliated with Apple or Microsoft. macOS is a trademark of Apple Inc.</span>
      </div>
    </footer>
  )
}
