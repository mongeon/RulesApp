# Azure OpenAI - Now Mandatory

## Summary

Azure OpenAI is now **required** infrastructure for RulesApp. The chat endpoint uses AI-enhanced responses powered by Azure OpenAI with the **gpt-4o-mini** model.

## Model Details

- **Model**: gpt-4o-mini
- **Version**: 2024-07-18
- **Status**: Generally Available (GA)
- **Retirement Date**: March 31, 2026
- **Region**: East US 2 (default)

This model was selected based on Microsoft's current model availability documentation:
https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/model-retirements

## Changes Made

### Infrastructure (main.bicep)

1. **Removed `deployOpenAI` parameter** - OpenAI is no longer optional
2. **Made OpenAI resources unconditional** - Removed `if (deployOpenAI)` conditions
3. **Updated outputs** - Removed conditional logic from OpenAI outputs
4. **Model configuration**:
   - Model: `gpt-4o-mini`
   - Version: `2024-07-18`
   - Location: `eastus2`
   - SKU: `S0` (Standard)
   - Deployment SKU: `Standard` with capacity 20

### API (Program.cs)

1. **OpenAI configuration now required** - Throws exception if not configured:
   ```csharp
   var openAiEndpoint = builder.Configuration["OpenAI:Endpoint"]
       ?? throw new InvalidOperationException("OpenAI:Endpoint not configured");
   var openAiKey = builder.Configuration["OpenAI:Key"]
       ?? throw new InvalidOperationException("OpenAI:Key not configured");
   ```
2. **Default deployment name** updated to `gpt-4o-mini` (was `gpt-4`)

### Web UI (Chat.razor)

1. **AI mode enabled by default** - `UseAI: true` in chat requests
2. **Updated comment** - Changed from "Direct mode by default" to "AI-enhanced mode with OpenAI"

### Documentation Updates

1. **testing-milestone4.md**:
   - Changed "optional" to "required" for OpenAI prerequisite
   - Updated deployment name from `gpt-4` to `gpt-4o-mini`
   - Added model version specification (2024-07-18)
   - Removed note about optional OpenAI configuration

2. **MILESTONE4_SUMMARY.md**:
   - Updated to reflect mandatory OpenAI

## Deployment Impact

### What This Means

- **All environments** (local, dev, prod) now require Azure OpenAI
- **Cost increase**: Azure OpenAI pricing applies (S0 SKU)
- **Configuration required**: OpenAI credentials must be in local.settings.json and environment variables

### Migration Steps

1. **Deploy Azure OpenAI** (if not already deployed):
   ```powershell
   cd infra
   .\deploy.ps1 -Environment dev
   ```

2. **Update local.settings.json** with OpenAI credentials:
   ```json
   {
     "OpenAI:Endpoint": "https://your-openai.openai.azure.com",
     "OpenAI:Key": "your-key-here",
     "OpenAI:DeploymentName": "gpt-4o-mini"
   }
   ```

3. **Update Function App settings** in Azure:
   - Go to Azure Portal → Function App → Configuration
   - Add/update: `OpenAI:Endpoint`, `OpenAI:Key`, `OpenAI:DeploymentName`

## Rationale

### Why Mandatory?

1. **Core Feature**: Chat is a primary feature of RulesApp, and AI-enhanced responses provide the best user experience
2. **Citation Quality**: AI generation produces better-formatted answers with clearer citations
3. **User Expectations**: Users expect conversational AI assistance, not just raw text concatenation
4. **Simplified Architecture**: Removes optional/conditional logic from codebase

### Why gpt-4o-mini?

1. **Cost-effective**: Mini model is cheaper than full GPT-4 while maintaining good quality
2. **Fast responses**: Lower latency for user queries
3. **GA status**: Stable, production-ready model
4. **Long support window**: Won't retire until March 31, 2026
5. **Sufficient for task**: Rule citation and formatting don't require GPT-4 capabilities

## Fallback Options

While AI is now mandatory in infrastructure, the `ChatRequest` DTO still supports `UseAI: false` for testing or emergency fallback scenarios. This allows:

- Testing without AI calls
- Debugging retrieval/precedence logic
- Emergency fallback if OpenAI quota is exhausted

To use direct mode in UI, you would need to add a toggle or modify the Chat.razor code to set `UseAI: false`.

## Cost Considerations

### Expected Usage

- **Target users**: Small regional association (low traffic)
- **Estimated queries**: ~100-500/month initially
- **Azure OpenAI Pricing** (gpt-4o-mini):
  - Input: $0.000150 per 1K tokens
  - Output: $0.000600 per 1K tokens
  - Typical query: ~1K input + ~500 output tokens
  - Cost per query: ~$0.0005 USD
  - Monthly cost (500 queries): ~$0.25 USD

### Cost is negligible for target use case.

## Next Steps

1. ✅ Infrastructure updated
2. ✅ API configuration updated
3. ✅ Web UI updated
4. ✅ Documentation updated
5. ⏳ Deploy to dev environment with OpenAI
6. ⏳ Test AI-enhanced chat responses
7. ⏳ Deploy to prod environment

## References

- [Azure OpenAI Model Retirements](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/model-retirements?view=foundry-classic&tabs=text)
- [Azure OpenAI Pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/)
- [gpt-4o-mini Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models)
