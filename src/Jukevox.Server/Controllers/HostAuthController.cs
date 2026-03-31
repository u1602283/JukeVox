using Microsoft.AspNetCore.Mvc;
using Fido2NetLib;
using Fido2NetLib.Objects;
using JukeVox.Server.Extensions;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/host")]
public class HostAuthController : ControllerBase
{
    private readonly Fido2 _fido2;
    private readonly Fido2Configuration _fido2Config;
    private readonly HostCredentialService _credentialService;

    public HostAuthController(Fido2 fido2, Fido2Configuration fido2Config, HostCredentialService credentialService)
    {
        _fido2 = fido2;
        _fido2Config = fido2Config;
        _credentialService = credentialService;
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
    public IActionResult GetSetupStatus()
    {
        return Ok(new { available = _credentialService.IsSetupAvailable });
    }

    // --- First-time setup (creates admin) ---

    [HttpPost("setup/begin")]
    public IActionResult SetupBegin([FromBody] SetupBeginRequest request)
    {
        if (!_credentialService.IsSetupAvailable)
            return BadRequest(new { error = "Setup is not available" });

        if (!_credentialService.IsSetupTokenValid(request.Token))
            return Unauthorized(new { error = "Invalid setup token" });

        var hostId = Guid.NewGuid().ToString("N")[..8];
        var user = new Fido2User
        {
            Id = System.Text.Encoding.UTF8.GetBytes($"host-{hostId}"),
            Name = request.DisplayName ?? _fido2Config.ServerName,
            DisplayName = request.DisplayName ?? _fido2Config.ServerName
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
        _credentialService.StorePendingChallenge(sessionId, new PendingRegistration
        {
            Options = options, HostId = hostId, IsAdmin = true, DisplayName = request.DisplayName ?? "Admin"
        });

        return Ok(options);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> SetupComplete([FromBody] AuthenticatorAttestationRawResponse attestation)
    {
        if (_credentialService.HasAnyCredential)
            return BadRequest(new { error = "Setup already completed. Use invite codes to register new hosts." });

        var sessionId = HttpContext.GetSessionId();
        var pending = _credentialService.GetPendingChallenge<PendingRegistration>(sessionId);
        if (pending == null)
            return BadRequest(new { error = "No pending registration challenge. Please start setup again." });

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = pending.Options,
            IsCredentialIdUniqueToUserCallback = async (_, _) => true
        }, HttpContext.RequestAborted);

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
            return BadRequest(new { error = "No admin exists yet. Use /host/setup first." });

        if (!_credentialService.ValidateAndConsumeInviteCode(request.InviteCode))
            return Unauthorized(new { error = "Invalid or expired invite code" });

        var hostId = Guid.NewGuid().ToString("N")[..8];
        var user = new Fido2User
        {
            Id = System.Text.Encoding.UTF8.GetBytes($"host-{hostId}"),
            Name = request.DisplayName,
            DisplayName = request.DisplayName
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
        _credentialService.StorePendingChallenge(sessionId, new PendingRegistration
        {
            Options = options, HostId = hostId, IsAdmin = false, DisplayName = request.DisplayName
        });

        return Ok(options);
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> RegisterComplete([FromBody] AuthenticatorAttestationRawResponse attestation)
    {
        var sessionId = HttpContext.GetSessionId();
        var pending = _credentialService.GetPendingChallenge<PendingRegistration>(sessionId);
        if (pending == null)
            return BadRequest(new { error = "No pending registration challenge. Please start again." });

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = pending.Options,
            IsCredentialIdUniqueToUserCallback = async (_, _) => true
        }, HttpContext.RequestAborted);

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
            return BadRequest(new { error = "No host credentials registered" });

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
            return BadRequest(new { error = "No pending login challenge. Please try again." });

        // Find which host credential matches
        var credential = _credentialService.GetCredentialByCredentialIdString(assertion.Id);
        if (credential == null)
            return BadRequest(new { error = "Unknown credential" });

        var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertion,
            OriginalOptions = options,
            StoredPublicKey = credential.PublicKey,
            StoredSignatureCounter = credential.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = async (_, _) => true
        }, HttpContext.RequestAborted);

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
            return Forbid();

        var hosts = _credentialService.GetAllCredentials()
            .Select(c => new { hostId = c.HostId, displayName = c.DisplayName, isAdmin = c.IsAdmin, createdAt = c.CreatedAt })
            .ToList();
        return Ok(hosts);
    }

    [HttpDelete("hosts/{targetHostId}")]
    public IActionResult DeleteHost(string targetHostId)
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null || !_credentialService.IsAdmin(hostId))
            return Forbid();

        if (targetHostId == hostId)
            return BadRequest(new { error = "Cannot delete your own credential" });

        if (!_credentialService.DeleteCredential(targetHostId))
            return NotFound(new { error = "Host not found" });

        return NoContent();
    }

    // --- Invite codes (admin only) ---

    [HttpPost("invite-codes")]
    public IActionResult GenerateInviteCode()
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null || !_credentialService.IsAdmin(hostId))
            return Forbid();

        var code = _credentialService.GenerateInviteCode();
        return Ok(new { code });
    }

    // --- Logout ---

    [HttpPost("logout")]
    public IActionResult Logout()
    {
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
}
