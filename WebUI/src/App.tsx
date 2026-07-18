import { useEffect, useMemo } from 'react'
import { wsClient } from './lib/wsClient'
import { GraphEditorLayout } from './components/GraphEditorLayout'
import './App.css'

export default function App() {
  const initialGraphName = useMemo(() => {
    if (typeof window === 'undefined') return undefined
    const params = new URLSearchParams(window.location.search)
    const graphName = params.get('graph') ?? undefined
    return graphName
  }, [])

  useEffect(() => {
    wsClient.connect()
    return () => wsClient.disconnect()
  }, [])

  return <GraphEditorLayout initialGraphName={initialGraphName} />
}
