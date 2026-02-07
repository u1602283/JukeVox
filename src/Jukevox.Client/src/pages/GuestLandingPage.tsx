import { Link } from 'react-router-dom';
import { JoinForm } from '../components/JoinForm';

export function GuestLandingPage() {
  return (
    <div className="landing-page">
      <h1>JukeVox</h1>
      <p className="subtitle">Collaborative music for your party</p>

      <div className="panel">
        <JoinForm />
      </div>

      <Link to="/host" className="host-login-link">
        Host Login
      </Link>
    </div>
  );
}
