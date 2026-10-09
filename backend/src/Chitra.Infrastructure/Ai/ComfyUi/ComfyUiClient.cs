using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Chitra.Application.Common;
using Chitra.Application.Settings;

namespace Chitra.Infrastructure.Ai.ComfyUi;

/// <summary>Thin HTTP client for the ComfyUI server API. The base URL is read from settings on every call.</summary>
public sealed class ComfyUiClient(HttpClient http, IRuntimeSettings settings)
{
    public Uri BaseUri
    {
        get
        {
            var url = settings.Current.ComfyUiUrl.TrimEnd('/') + "/";
            return new Uri(url, UriKind.Absolute);
        }
    }

    public string NotRunningMessage =>
        $"ComfyUI is not running. Start ComfyUI at {BaseUri.ToString().TrimEnd('/')} and try again.";

    /// <summary>POST /prompt — queues a workflow and returns its prompt_id.</summary>
    public async Task<string> QueuePromptAsync(JsonObject workflow, string clientId, CancellationToken ct)
    {
        var body = new JsonObject { ["prompt"] = workflow, ["client_id"] = clientId };
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url("prompt"))
        {
            Content = JsonContent.Create(body),
        }, ct);

        var json = await ReadJsonAsync(response, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ComfyUiErrors.FromPromptRejection(json, (int)response.StatusCode);
        }

        if (json?["node_errors"] is JsonObject nodeErrors && nodeErrors.Count > 0)
        {
            throw ComfyUiErrors.FromPromptRejection(json, 400);
        }

        return json?["prompt_id"]?.GetValue<string>()
               ?? throw new AppException(ErrorCode.InvalidWorkflow, "ComfyUI accepted the workflow but did not return a prompt id.");
    }

    /// <summary>GET /history/{promptId} — returns the history entry or null when ComfyUI has none (yet).</summary>
    public async Task<JsonObject?> GetHistoryAsync(string promptId, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url($"history/{Uri.EscapeDataString(promptId)}")), ct);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response, ct);
        return json?[promptId] as JsonObject;
    }

    /// <summary>GET /queue — prompt ids currently running and pending.</summary>
    public async Task<(IReadOnlyList<string> Running, IReadOnlyList<string> Pending)> GetQueueAsync(CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("queue")), ct);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response, ct);

        // Each queue item is [number, prompt_id, prompt, extra_data, outputs_to_execute].
        static List<string> Ids(JsonNode? node) =>
            node is JsonArray items
                ? items.OfType<JsonArray>().Select(i => i.Count > 1 ? i[1]?.GetValue<string>() : null).OfType<string>().ToList()
                : [];

        return (Ids(json?["queue_running"]), Ids(json?["queue_pending"]));
    }

    /// <summary>GET /view — downloads an output file. Dispose the returned stream to release the connection.</summary>
    public async Task<Stream> ViewAsync(string query, CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url($"view?{query}")), ct, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new AppException(ErrorCode.GenerationFailed, "ComfyUI reported the output file as missing. It may have been deleted from ComfyUI's output folder.");
        }

        response.EnsureSuccessStatusCode();
        return new ResponseStream(await response.Content.ReadAsStreamAsync(ct), response);
    }

    /// <summary>GET /system_stats.</summary>
    public async Task<JsonObject> GetSystemStatsAsync(CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("system_stats")), ct);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response, ct) as JsonObject ?? [];
    }

    /// <summary>GET /object_info/{nodeClass} — node definition, including selectable model files.</summary>
    public async Task<JsonObject?> GetObjectInfoAsync(string nodeClass, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url($"object_info/{Uri.EscapeDataString(nodeClass)}")), ct);
        if (!response.IsSuccessStatusCode) return null;
        return (await ReadJsonAsync(response, ct))?[nodeClass] as JsonObject;
    }

    /// <summary>POST /upload/image — uploads an input image and returns the name to use in a LoadImage node.</summary>
    public async Task<string> UploadImageAsync(string filePath, CancellationToken ct)
    {
        using var response = await SendAsync(() =>
        {
            var form = new MultipartFormDataContent();
            var file = new StreamContent(File.OpenRead(filePath));
            file.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png",
            });
            form.Add(file, "image", $"chitra_{Guid.NewGuid():N}{Path.GetExtension(filePath)}");
            form.Add(new StringContent("true"), "overwrite");
            form.Add(new StringContent("input"), "type");
            return new HttpRequestMessage(HttpMethod.Post, Url("upload/image")) { Content = form };
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(ErrorCode.GenerationFailed, $"ComfyUI rejected the input image upload ({(int)response.StatusCode}).");
        }

        var json = await ReadJsonAsync(response, ct);
        var name = json?["name"]?.GetValue<string>() ?? throw new AppException(ErrorCode.GenerationFailed, "ComfyUI did not return the uploaded image name.");
        var subfolder = json?["subfolder"]?.GetValue<string>();
        return string.IsNullOrEmpty(subfolder) ? name : $"{subfolder}/{name}";
    }

    /// <summary>POST /interrupt — stops the running prompt (targeted by id on recent ComfyUI versions).</summary>
    public async Task InterruptAsync(string promptId, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url("interrupt"))
        {
            Content = JsonContent.Create(new JsonObject { ["prompt_id"] = promptId }),
        }, ct);
    }

    /// <summary>POST /queue {"delete": [...]} — removes pending prompts.</summary>
    public async Task DeleteFromQueueAsync(string promptId, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url("queue"))
        {
            Content = JsonContent.Create(new JsonObject { ["delete"] = new JsonArray(promptId) }),
        }, ct);
    }

    private Uri Url(string relative) => new(BaseUri, relative);

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = requestFactory();
        try
        {
            return await http.SendAsync(request, completion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AppException(ErrorCode.ProviderUnavailable, NotRunningMessage, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AppException(ErrorCode.ProviderUnavailable, $"ComfyUI at {BaseUri} did not respond in time. It may be busy loading a model or not running.", ex);
        }
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject { ["error"] = new JsonObject { ["message"] = text.Length > 500 ? text[..500] : text } };
        }
    }

    /// <summary>Keeps the HTTP response alive for as long as its content stream is being read.</summary>
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
