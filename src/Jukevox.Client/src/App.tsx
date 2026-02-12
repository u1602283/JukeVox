import { useState, useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { PartyProvider } from './context/PartyContext';
import { useParty } from './hooks/useParty';
import { api } from './api/client';
import { GuestLandingPage } from './pages/GuestLandingPage';
import { PartyPage } from './pages/PartyPage';
import { HostSetupPage } from './pages/HostSetupPage';
import { HostPortalPage } from './pages/HostPortalPage';

function GuestRoute() {
  const { party, loading } = useParty();
  const [isHost, setIsHost] = useState<boolean | null>(null);

  useEffect(() => {
    api.hostStatus().then(s => setIsHost(s.authenticated)).catch(() => setIsHost(false));
  }, []);

  if (loading || isHost === null) {
    return <div className="loading">Loading...</div>;
  }

  if (isHost) {
    return <Navigate to="/host" replace />;
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
