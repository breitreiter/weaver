import { BrowserRouter, Routes, Route } from 'react-router-dom'
import Workbench from './Workbench'
import './App.css'

// The workbench: search (forage) left, board (sensemake) right. Board URLs are
// /view?board=<id> — both paths land on the workbench.
export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Workbench />} />
        <Route path="/view" element={<Workbench />} />
      </Routes>
    </BrowserRouter>
  )
}
