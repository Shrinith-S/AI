using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Chitra.Tests.Integration;

/// <summary>
/// Test double that speaks ComfyUI's HTTP protocol (/system_stats, /object_info, /prompt, /history, /queue, /view,
/// /upload/image). It is only used by tests to exercise the real client, provider and worker end to end.
/// </summary>
public sealed class FakeComfyUi : HttpMessageHandler
{
    public static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly ConcurrentDictionary<string, int> _historyPolls = new();
    private int _promptCounter;

    public bool Available { get; set; } = true;

    /// <summary>Number of /history polls before a prompt is reported complete.</summary>
    public int PollsUntilComplete { get; set; } = 2;

    public string? ExecutionError { get; set; }

    public List<JsonObject> SubmittedWorkflows { get; } = [];
    public List<string> UploadedFiles { get; } = [];

    public string[] Checkpoints { get; set; } = ["SDXL/sd_xl_base_1.0.safetensors", "v1-5-pruned-emaonly-fp16.safetensors", "ltx-video-2b-v0.9.5.safetensors"];
    public string[] TextEncoders { get; set; } = ["t5xxl_fp8_e4m3fn.safetensors"];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        }

        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var method = request.Method.Method;

        return (method, path) switch
        {
            ("GET", "system_stats") => Json("""
                { "system": { "comfyui_version": "0.3.60", "os": "nt" },
                  "devices": [ { "name": "cuda:0 NVIDIA GeForce RTX 4070", "type": "cuda", "vram_total": 12884901888, "vram_free": 11000000000 } ] }
                """),
            ("GET", var p) when p.StartsWith("object_info/") => ObjectInfo(p["object_info/".Length..]),
            ("POST", "prompt") => await QueuePrompt(request, cancellationToken),
            ("GET", var p) when p.StartsWith("history/") => History(p["history/".Length..]),
            ("GET", "queue") => Json("""{ "queue_running": [], "queue_pending": [] }"""),
            ("GET", "view") => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PngBytes) },
            ("POST", "upload/image") => UploadImage(),
            ("POST", "interrupt") or ("POST", "queue") => new HttpResponseMessage(HttpStatusCode.OK),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage ObjectInfo(string node)
    {
        // Mix the legacy and the newer COMBO formats, as real ComfyUI versions differ.
        string[] files = node switch
        {
            "CheckpointLoaderSimple" => Checkpoints,
            "CLIPLoader" => TextEncoders,
            _ => [],
        };

        var list = new JsonArray(files.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray());
        var input = node == "CLIPLoader"
            ? new JsonArray("COMBO", new JsonObject { ["options"] = list })
            : new JsonArray(list, new JsonObject());
        var inputName = node switch
        {
            "CheckpointLoaderSimple" => "ckpt_name",
            "UNETLoader" => "unet_name",
            "VAELoader" => "vae_name",
            "LoraLoader" => "lora_name",
            _ => "clip_name",
        };

        var json = new JsonObject
        {
            [node] = new JsonObject { ["input"] = new JsonObject { ["required"] = new JsonObject { [inputName] = input } } },
        };
        return Json(json.ToJsonString());
    }

    private async Task<HttpResponseMessage> QueuePrompt(HttpRequestMessage request, CancellationToken ct)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
        lock (SubmittedWorkflows) SubmittedWorkflows.Add(body["prompt"]!.AsObject());
        var id = $"prompt-{Interlocked.Increment(ref _promptCounter)}";
        return Json($$"""{ "prompt_id": "{{id}}", "number": 1, "node_errors": {} }""");
    }

    private HttpResponseMessage History(string promptId)
    {
        var polls = _historyPolls.AddOrUpdate(promptId, 1, (_, n) => n + 1);
        if (polls < PollsUntilComplete) return Json("{}");

        if (ExecutionError is not null)
        {
            return Json($$"""
                { "{{promptId}}": { "status": { "status_str": "error", "completed": false, "messages": [
                  ["execution_error", { "prompt_id": "{{promptId}}", "node_type": "KSampler", "exception_type": "RuntimeError", "exception_message": "{{ExecutionError}}" }] ] },
                  "outputs": {} } }
                """);
        }

        JsonObject workflow;
        lock (SubmittedWorkflows) workflow = SubmittedWorkflows[^1];
        var isVideo = workflow.Any(n => n.Value?["class_type"]?.GetValue<string>() == "SaveVideo");
        var output = isVideo
            ? """{ "11": { "images": [ { "filename": "chitra_00001_.mp4", "subfolder": "chitra", "type": "output" } ], "animated": [ true ] } }"""
            : """{ "9": { "images": [ { "filename": "chitra_00001_.png", "subfolder": "chitra", "type": "output" } ] } }""";

        return Json($$"""{ "{{promptId}}": { "status": { "status_str": "success", "completed": true, "messages": [] }, "outputs": {{output}} } }""");
    }

    private HttpResponseMessage UploadImage()
    {
        var name = $"upload_{UploadedFiles.Count}.png";
        UploadedFiles.Add(name);
        return Json($$"""{ "name": "{{name}}", "subfolder": "", "type": "input" }""");
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
