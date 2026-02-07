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
        return Ok(new
        {
            authenticated = HttpContext.IsHostAuthenticated(),
            hasCredential = _credentialService.HasCredential,
            setupAvailable = _credentialService.IsSetupAvailable
        });
    }

    [HttpGet("setup/status")]
    public IActionResult GetSetupStatus()
    {
        return Ok(new { available = _credentialService.IsSetupAvailable });
    }

    [HttpPost("setup/begin")]
    public IActionResult SetupBegin([FromBody] SetupBeginRequest request)
    {
        if (!_credentialService.IsSetupAvailable)
            return BadRequest(new { error = "Setup is not available" });

        if (!_credentialService.IsSetupTokenValid(request.Token))
            return Unauthorized(new { error = "Invalid setup token" });

        var serverName = _fido2Config.ServerName;
        var user = new Fido2User
        {
            Id = System.Text.Encoding.UTF8.GetBytes($"{serverName}-host"),
            Name = serverName,
            DisplayName = serverName
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
        _credentialService.StorePendingChallenge(sessionId, options);

        return Ok(options);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> SetupComplete([FromBody] AuthenticatorAttestationRawResponse attestation)
    {
        if (_credentialService.HasCredential)
            return BadRequest(new { error = "Host credential already registered" });

        var sessionId = HttpContext.GetSessionId();
        var options = _credentialService.GetPendingChallenge<CredentialCreateOptions>(sessionId);
        if (options == null)
            return BadRequest(new { error = "No pending registration challenge. Please start setup again." });

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (_, _) => true
        }, HttpContext.RequestAborted);

        var dnsRecord = _credentialService.SaveCredential(new HostCredential
        {
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            SignCount = result.SignCount
        });

        // Auto-authenticate after registration
        HttpContext.SetHostAuthCookie();

        return Ok(new { success = true, dnsRecord });
    }

    [HttpPost("login/begin")]
    public IActionResult LoginBegin()
    {
        var credential = _credentialService.GetCredential();
        if (credential == null)
            return BadRequest(new { error = "No host credential registered" });

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [new PublicKeyCredentialDescriptor(credential.CredentialId)],
            UserVerification = UserVerificationRequirement.Preferred
        });

        var sessionId = HttpContext.GetSessionId();
        _credentialService.StorePendingChallenge(sessionId, options);

        return Ok(options);
    }

    [HttpPost("login/complete")]
    public async Task<IActionResult> LoginComplete([FromBody] AuthenticatorAssertionRawResponse assertion)
    {
        var credential = _credentialService.GetCredential();
        if (credential == null)
            return BadRequest(new { error = "No host credential registered" });

        var sessionId = HttpContext.GetSessionId();
        var options = _credentialService.GetPendingChallenge<AssertionOptions>(sessionId);
        if (options == null)
            return BadRequest(new { error = "No pending login challenge. Please try again." });

        var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertion,
            OriginalOptions = options,
            StoredPublicKey = credential.PublicKey,
            StoredSignatureCounter = credential.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = async (_, _) => true
        }, HttpContext.RequestAborted);

        _credentialService.UpdateSignCount(result.SignCount);
        HttpContext.SetHostAuthCookie();

        return Ok(new { success = true });
    }

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
}
