import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { PartyProvider } from './context/PartyContext';
import { useParty } from './hooks/useParty';
import { GuestLandingPage } from './pages/GuestLandingPage';
import { PartyPage } from './pages/PartyPage';
import { HostSetupPage } from './pages/HostSetupPage';
import { HostPortalPage } from './pages/HostPortalPage';

function GuestRoute() {
  const { party, loading } = useParty();

  if (loading) {
    return <div className="loading">Loading...</div>;
  }

  return party ? <PartyPage /> : <GuestLandingPage />;
}

function App() {
  return (
    <BrowserRouter>
      <PartyProvider>
        <Routes>
          <Route path="/" element={<GuestRoute />} />
          <Route path="/host/setup" element={<HostSetupPage />} />
          <Route path="/host" element={<HostPortalPage />} />
        </Routes>
      </PartyProvider>
    </BrowserRouter>
  );
}

export default App;
