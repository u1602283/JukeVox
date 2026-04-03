using System.Net;
using System.Text;

namespace JukeVox.Server.Tests.Helpers;

public class MockHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Content)> _responses = new();
    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public void EnqueueResponse(HttpStatusCode status, string jsonContent)
    {
        _responses.Enqueue((status, jsonContent));
    }

    public void EnqueueSuccess(string jsonContent)
    {
        EnqueueResponse(HttpStatusCode.OK, jsonContent);
    }

    public void EnqueueNoContent()
    {
        _responses.Enqueue((HttpStatusCode.NoContent, ""));
    }

    public void EnqueueError(HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        _responses.Enqueue((status, """{"error":"test error"}"""));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (_responses.Count == 0)
            throw new InvalidOperationException($"No more mock responses queued. Request: {request.Method} {request.RequestUri}");

        var (status, content) = _responses.Dequeue();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
