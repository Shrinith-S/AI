using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Settings;

namespace Chitra.Application.Prompting;

/// <summary>Selects the configured prompt enhancer, falling back to the free rule-based one.</summary>
public sealed class PromptService(IEnumerable<IPromptEnhancer> enhancers, IRuntimeSettings settings)
{
    public IReadOnlyList<string> Available => enhancers.Select(e => e.Name).ToList();

    public async Task<PromptEnhancementDto> EnhanceAsync(EnhancePromptRequest request, CancellationToken cancellationToken)
    {
        new Validator().NotEmpty(request.Prompt, "prompt", 2000).ThrowIfInvalid();

        var enhancer = Resolve();
        var result = await enhancer.EnhanceAsync(
            new PromptEnhancementRequest(request.Prompt, request.Type, request.StyleHint, request.Variation), cancellationToken);
        return new PromptEnhancementDto(request.Prompt, result.Prompt, result.NegativePrompt, result.Additions, result.Enhancer);
    }

    internal IPromptEnhancer Resolve()
    {
        var current = settings.Current;
        var enhancer = enhancers.FirstOrDefault(e => e.Name.Equals(current.PromptEnhancer, StringComparison.OrdinalIgnoreCase))
                       ?? enhancers.First(e => e is RuleBasedPromptEnhancer);

        if (current.LocalOnly && (enhancer.IncursApiCharges || !enhancer.IsLocal))
        {
            throw new AppException(ErrorCode.LocalOnlyViolation,
                $"The '{enhancer.Name}' prompt enhancer calls a paid/remote API, which is blocked while Local-Only mode is on.");
        }

        return enhancer;
    }
}
