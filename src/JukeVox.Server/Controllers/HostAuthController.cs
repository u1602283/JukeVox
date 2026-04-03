using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/host")]
public class HostAuthController : ControllerBase
{
    private readonly HostCredentialService _credentialService;
    private readonly Fido2 _fido2;
    private readonly Fido2Configuration _fido2Config;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;
    private readonly IPartyService _partyService;

    public HostAuthController(Fido2 fido2,
        Fido2Configuration fido2Config,
        HostCredentialService credentialService,
        IPartyService partyService,
        IHubContext<PartyHub, IPartyClient> hubContext)
    {
        _fido2 = fido2;
        _fido2Config = fido2Config;
        _credentialService = credentialService;
        _partyService = partyService;
        _hubContext = hubContext;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        return Ok(new
        {
            authenticated = hostId != null,
            hostId,
            isAdmin = hostId != null && _credentialService.IsAdmin(hostId),
            hasCredential = _credentialService.HasAnyCredential,
            setupAvailable = _credentialService.IsSetupAvailable
        });
    }

    [HttpGet("setup/status")]
    public IActionResult GetSetupStatus() => Ok(new { available = _credentialService.IsSetupAvailable });

    // --- First-time setup (creates admin) ---

    [HttpPost("setup/begin")]
    public IActionResult SetupBegin([FromBody] SetupBeginRequest request)
    {
        if (!_credentialService.IsSetupAvailable)
        {
            return BadRequest(new { error = "Setup is not available" });
        }

        if (!_credentialService.IsSetupTokenValid(request.Token))
        {
            return Unauthorized(new { error = "Invalid setup token" });
        }

        var displayName = request.DisplayName?.Trim();
        if (displayName != null && displayName.Length > 30)
        {
            return BadRequest(new { error = "Display name must be 30 characters or fewer" });
        }

        var hostId = Guid.NewGuid().ToString("N")[..8];
        var user = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes($"host-{hostId}"),
            Name = displayName ?? _fido2Config.ServerName,
            DisplayName = displayName ?? _fido2Config.ServerName
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = [],
            AttestationPreference = AttestationConveyancePreference.None,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Preferred
            }
        });

        var sessionId = HttpContext.GetSessionId();
        _credentialService.StorePendingChallenge(sessionId,
            new PendingRegistration
            {
                Options = options, HostId = hostId, IsAdmin = true, DisplayName = displayName ?? "Admin"
            });

        return Ok(options);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> SetupComplete([FromBody] AuthenticatorAttestationRawResponse attestation)
    {
        if (_credentialService.HasAnyCredential)
        {
            return BadRequest(new { error = "Setup already completed. Use invite codes to register new hosts." });
        }

        var sessionId = HttpContext.GetSessionId();
        var pending = _credentialService.GetPendingChallenge<PendingRegistration>(sessionId);
        if (pending == null)
        {
            return BadRequest(new { error = "No pending registration challenge. Please start setup again." });
        }

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = pending.Options,
                IsCredentialIdUniqueToUserCallback = async (args, _) =>
                    _credentialService.GetCredentialByCredentialId(args.CredentialId) == null
            },
            HttpContext.RequestAborted);

        _credentialService.SaveCredential(new HostCredential
        {
            HostId = pending.HostId,
            DisplayName = pending.DisplayName,
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            SignCount = result.SignCount,
            IsAdmin = pending.IsAdmin
        });

        HttpContext.SetHostAuthCookie(pending.HostId);

        return Ok(new { success = true, hostId = pending.HostId });
    }

    // --- Registration via invite code ---

    [HttpPost("register/begin")]
    public IActionResult RegisterBegin([FromBody] RegisterBeginRequest request)
    {
        if (!_credentialService.HasAnyCredential)
        {
            return BadRequest(new { error = "No admin exists yet. Use /host/setup first." });
        }

        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > 30)
        {
            return BadRequest(new { error = "Display name must be between 1 and 30 characters" });
        }

        if (!_credentialService.IsInviteCodeValid(request.InviteCode))
        {
            return Unauthorized(new { error = "Invalid or expired invite code" });
        }

        var hostId = Guid.NewGuid().ToString("N")[..8];
        var user = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes($"host-{hostId}"),
            Name = displayName,
            DisplayName = displayName
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = [],
            AttestationPreference = AttestationConveyancePreference.None,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Preferred
            }
        });

        var sessionId = HttpContext.GetSessionId();
        _credentialService.StorePendingChallenge(sessionId,
            new PendingRegistration
            {
                Options = options, HostId = hostId, IsAdmin = false, DisplayName = displayName,
                InviteCode = request.InviteCode
            });

        return Ok(options);
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> RegisterComplete([FromBody] AuthenticatorAttestationRawResponse attestation)
    {
        var sessionId = HttpContext.GetSessionId();
        var pending = _credentialService.GetPendingChallenge<PendingRegistration>(sessionId);
        if (pending == null)
        {
            return BadRequest(new { error = "No pending registration challenge. Please start again." });
        }

        if (pending.InviteCode == null || !_credentialService.ValidateAndConsumeInviteCode(pending.InviteCode))
        {
            return Unauthorized(new { error = "Invite code is no longer valid. Please request a new one." });
        }

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = pending.Options,
                IsCredentialIdUniqueToUserCallback = async (args, _) =>
                    _credentialService.GetCredentialByCredentialId(args.CredentialId) == null
            },
            HttpContext.RequestAborted);

        _credentialService.SaveCredential(new HostCredential
        {
            HostId = pending.HostId,
            DisplayName = pending.DisplayName,
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            SignCount = result.SignCount,
            IsAdmin = pending.IsAdmin
        });

        HttpContext.SetHostAuthCookie(pending.HostId);

        return Ok(new { success = true, hostId = pending.HostId });
    }

    // --- Login (works for any registered host) ---

    [HttpPost("login/begin")]
    public IActionResult LoginBegin()
    {
        var credentials = _credentialService.GetAllCredentials();
        if (credentials.Count == 0)
        {
            return BadRequest(new { error = "No host credentials registered" });
        }

        var allowedCredentials = credentials
            .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
            .ToList();

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Preferred
        });

        var sessionId = HttpContext.GetSessionId();
        _credentialService.StorePendingChallenge(sessionId, options);

        return Ok(options);
    }

    [HttpPost("login/complete")]
    public async Task<IActionResult> LoginComplete([FromBody] AuthenticatorAssertionRawResponse assertion)
    {
        var sessionId = HttpContext.GetSessionId();
        var options = _credentialService.GetPendingChallenge<AssertionOptions>(sessionId);
        if (options == null)
        {
            return BadRequest(new { error = "No pending login challenge. Please try again." });
        }

        // Find which host credential matches
        var credential = _credentialService.GetCredentialByCredentialIdString(assertion.Id);
        if (credential == null)
        {
            return BadRequest(new { error = "Unknown credential" });
        }

        var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion,
                OriginalOptions = options,
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = credential.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = async (_, _) => true
            },
            HttpContext.RequestAborted);

        _credentialService.UpdateSignCount(credential.HostId, result.SignCount);
        HttpContext.SetHostAuthCookie(credential.HostId);

        return Ok(new { success = true, hostId = credential.HostId, isAdmin = credential.IsAdmin });
    }

    // --- Host management (admin only) ---

    [HttpGet("hosts")]
    public IActionResult GetHosts()
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null || !_credentialService.IsAdmin(hostId))
        {
            return Forbid();
        }

        var hosts = _credentialService.GetAllCredentials()
            .Select(c => new
                { hostId = c.HostId, displayName = c.DisplayName, isAdmin = c.IsAdmin, createdAt = c.CreatedAt })
            .ToList();
        return Ok(hosts);
    }

    [HttpDelete("hosts/{targetHostId}")]
    public async Task<IActionResult> DeleteHost(string targetHostId)
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null || !_credentialService.IsAdmin(hostId))
        {
            return Forbid();
        }

        if (targetHostId == hostId)
        {
            return BadRequest(new { error = "Cannot delete your own credential" });
        }

        if (!_credentialService.DeleteCredential(targetHostId))
        {
            return NotFound(new { error = "Host not found" });
        }

        // End all active parties owned by the deleted host and notify clients
        var parties = _partyService.GetPartiesForHost(targetHostId);
        foreach (var party in parties)
        {
            await _hubContext.Clients.Group(party.Id).PartyEnded();
            _partyService.EndParty(party.Id);
        }

        return NoContent();
    }

    // --- Invite codes (admin only) ---

    [HttpPost("invite-codes")]
    public IActionResult GenerateInviteCode()
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null || !_credentialService.IsAdmin(hostId))
        {
            return Forbid();
        }

        var code = _credentialService.GenerateInviteCode();
        return Ok(new { code });
    }

    // --- Logout ---

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var sessionId = HttpContext.GetSessionId();
        _partyService.UnmapSession(sessionId);
        HttpContext.ClearHostAuthCookie();
        return Ok(new { success = true });
    }
}

public class SetupBeginRequest
{
    public required string Token { get; set; }
    public string? DisplayName { get; set; }
}

public class RegisterBeginRequest
{
    public required string InviteCode { get; set; }
    public required string DisplayName { get; set; }
}

public class PendingRegistration
{
    public required CredentialCreateOptions Options { get; set; }
    public required string HostId { get; set; }
    public required bool IsAdmin { get; set; }
    public required string DisplayName { get; set; }
    public string? InviteCode { get; set; }
}
