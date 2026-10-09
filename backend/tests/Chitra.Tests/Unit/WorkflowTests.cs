using System.Text.Json.Nodes;
using Chitra.Application.Common;
using Chitra.Domain.Enums;
using Chitra.Infrastructure;
using Chitra.Infrastructure.Ai;
using Chitra.Infrastructure.Ai.ComfyUi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chitra.Tests.Unit;

public class WorkflowTests
{
    private static readonly IOptions<AiOptions> Options = Microsoft.Extensions.Options.Options.Create(new AiOptions());

    [Fact]
    public void Renderer_ReplacesTypedAndInlinePlaceholders_AndStripsComments()
    {
        var template = JsonNode.Parse("""
            {
              "_comment": "ignored",
              "1": { "class_type": "KSampler", "inputs": { "seed": "{{seed}}", "cfg": "{{cfg}}", "flag": "{{flag}}", "label": "run-{{seed}}", "link": ["2", 0] } }
            }
            """)!.AsObject();

        var result = WorkflowRenderer.Render(template, new Dictionary<string, object?> { ["seed"] = 42L, ["cfg"] = 6.5, ["flag"] = true });

        Assert.False(result.ContainsKey("_comment"));
        var inputs = result["1"]!["inputs"]!;
        Assert.Equal(42L, inputs["seed"]!.GetValue<long>());
        Assert.Equal(6.5, inputs["cfg"]!.GetValue<double>());
        Assert.True(inputs["flag"]!.GetValue<bool>());
        Assert.Equal("run-42", inputs["label"]!.GetValue<string>());
        Assert.Equal("2", inputs["link"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Renderer_RejectsUnknownPlaceholders()
    {
        var template = JsonNode.Parse("""{ "1": { "class_type": "X", "inputs": { "a": "{{missing}}" } } }""")!.AsObject();
        var ex = Assert.Throws<AppException>(() => WorkflowRenderer.Render(template, new Dictionary<string, object?>()));
        Assert.Equal(ErrorCode.InvalidWorkflow, ex.Code);
    }

    /// <summary>
    /// Every shipped workflow must render with the parameters the provider supplies, and every node link must
    /// point at an existing node output, otherwise ComfyUI would reject it.
    /// </summary>
    [Fact]
    public void AllCatalogWorkflows_RenderAndAreWellFormed()
    {
        var catalog = new JsonModelCatalog(Options, NullLogger<JsonModelCatalog>.Instance);
        var store = new FileWorkflowTemplateStore(Options);
        Assert.NotEmpty(catalog.All);

        foreach (var model in catalog.All)
        {
            foreach (var capability in model.Capabilities)
            {
                var file = model.GetWorkflow(capability);
                Assert.False(file is null, $"{model.Id} lacks a workflow for {capability}");

                var parameters = new Dictionary<string, object?>
                {
                    ["prompt"] = "p", ["negative_prompt"] = "n", ["seed"] = 1L, ["steps"] = 20, ["cfg"] = 5.0,
                    ["width"] = 512, ["height"] = 512, ["sampler"] = "euler", ["scheduler"] = "simple", ["denoise"] = 1.0,
                    ["shift"] = 8.0, ["filename_prefix"] = "x", ["batch_size"] = 1, ["frames"] = 49, ["fps"] = 24,
                    ["frame_rate"] = 24.0, ["motion_strength"] = 0.5, ["image"] = "in.png",
                };
                foreach (var f in model.Files) parameters[$"file.{f.Role}"] = f.FileName;

                var workflow = WorkflowRenderer.Render(store.Load(file!), parameters);
                var nodeIds = workflow.Select(p => p.Key).ToHashSet();

                foreach (var (id, node) in workflow)
                {
                    Assert.False(string.IsNullOrEmpty(node?["class_type"]?.GetValue<string>()), $"{file} node {id} has no class_type");
                    foreach (var (input, value) in node!["inputs"]!.AsObject())
                    {
                        if (value is JsonArray link && link.Count == 2 && link[0] is JsonValue v && v.TryGetValue<string>(out var target))
                        {
                            Assert.True(nodeIds.Contains(target), $"{file} node {id}.{input} links to missing node {target}");
                        }

                        Assert.False(value?.ToJsonString().Contains("{{") ?? false, $"{file} node {id}.{input} still has a placeholder");
                    }
                }

                var outputs = workflow.Select(p => p.Value!["class_type"]!.GetValue<string>()).ToList();
                var expectedSave = capability == GenerationType.TextToImage ? "SaveImage" : "SaveVideo";
                Assert.Contains(expectedSave, outputs);
                if (capability == GenerationType.ImageToVideo) Assert.Contains("LoadImage", outputs);
            }
        }
    }

    [Fact]
    public void ParseHistory_ReturnsOutputs_ForSuccess()
    {
        var history = JsonNode.Parse("""
            {
              "status": { "status_str": "success", "completed": true, "messages": [] },
              "outputs": {
                "9": { "images": [ { "filename": "chitra_00001_.png", "subfolder": "chitra", "type": "output" },
                                   { "filename": "preview.png", "subfolder": "", "type": "temp" } ] },
                "12": { "images": [ { "filename": "clip_00001_.mp4", "subfolder": "chitra", "type": "output" } ], "animated": [ true ] }
              }
            }
            """)!.AsObject();

        var status = ComfyUiProvider.ParseHistory(history);

        Assert.Equal(Application.Abstractions.Ai.ProviderJobState.Completed, status.State);
        Assert.Equal(2, status.Outputs.Count);
        Assert.Equal(MediaType.Image, status.Outputs[0].MediaType);
        Assert.Equal(MediaType.Video, status.Outputs[1].MediaType);
        Assert.Equal("filename=chitra_00001_.png&subfolder=chitra&type=output", status.Outputs[0].Locator);
    }

    [Fact]
    public void ParseHistory_MapsOutOfMemory_ToInsufficientVram()
    {
        var history = JsonNode.Parse("""
            {
              "status": { "status_str": "error", "completed": false, "messages": [
                ["execution_start", { "prompt_id": "p" }],
                ["execution_error", { "prompt_id": "p", "node_type": "KSampler", "exception_type": "torch.OutOfMemoryError", "exception_message": "CUDA out of memory. Tried to allocate 2.00 GiB" }]
              ] },
              "outputs": {}
            }
            """)!.AsObject();

        var status = ComfyUiProvider.ParseHistory(history);

        Assert.Equal(Application.Abstractions.Ai.ProviderJobState.Failed, status.State);
        Assert.Equal(ErrorCode.InsufficientVram, status.ErrorCode);
        Assert.Contains("ran out of memory", status.Error);
    }

    [Fact]
    public void PromptRejection_ForMissingCheckpoint_IsModelMissing()
    {
        var json = JsonNode.Parse("""
            {
              "error": { "type": "prompt_outputs_failed_validation", "message": "Prompt outputs failed validation" },
              "node_errors": { "4": { "class_type": "CheckpointLoaderSimple", "errors": [
                { "type": "value_not_in_list", "message": "Value not in list", "details": "ckpt_name: 'sd_xl_base_1.0.safetensors' not in ['other.safetensors']" } ] } }
            }
            """);

        var ex = ComfyUiErrors.FromPromptRejection(json, 400);

        Assert.Equal(ErrorCode.ModelMissing, ex.Code);
        Assert.Contains("sd_xl_base_1.0.safetensors", ex.Message);
    }

    [Fact]
    public void PromptRejection_ForMissingNode_IsInvalidWorkflow()
    {
        var json = JsonNode.Parse("""{ "error": { "type": "invalid_prompt", "message": "Cannot execute because node CreateVideo does not exist." }, "node_errors": {} }""");
        var ex = ComfyUiErrors.FromPromptRejection(json, 400);
        Assert.Equal(ErrorCode.InvalidWorkflow, ex.Code);
        Assert.Contains("Update ComfyUI", ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:8188", true)]
    [InlineData("http://127.0.0.1:8188", true)]
    [InlineData("http://comfyui:8188", true)]
    [InlineData("http://192.168.1.20:8188", true)]
    [InlineData("http://host.docker.internal:8188", true)]
    [InlineData("https://my-gpu.example-cloud.com", false)]
    [InlineData("http://8.8.8.8:8188", false)]
    public void LocalNetwork_ClassifiesUrls(string url, bool expected) =>
        Assert.Equal(expected, LocalNetwork.IsLocalUrl(url));
}
