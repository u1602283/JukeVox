import { PartyProvider, useParty } from './context/PartyContext';
import { LandingPage } from './pages/LandingPage';
import { PartyPage } from './pages/PartyPage';
import './App.css';

function AppContent() {
  const { party, loading } = useParty();

  if (loading) {
    return <div className="loading">Loading...</div>;
  }

  return party ? <PartyPage /> : <LandingPage />;
}

function App() {
  return (
    <PartyProvider>
      <AppContent />
    </PartyProvider>
  );
}

export default App;
