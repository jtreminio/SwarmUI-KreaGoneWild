using ComfyTyped.Core;
using ComfyTyped.Generated;
using ComfyTyped.SwarmUI;
using KreaGoneWild.Generated;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace KreaGoneWild;

public class KreaGoneWildExtension : Extension
{
    public const string RebalanceFeatureFlag = "conditioning_krea2_rebalance";
    public const string RebalanceNodeName = "ConditioningKrea2Rebalance";
    public const string RebalanceRepoUrl = "https://github.com/nova452/ComfyUI-ConditioningKrea2Rebalance";
    public const string DefaultPerLayerWeights = "1.0,1.0,1.0,1.0,1.0,1.0,1.0,2.5,5.0,1.1,4.0,1.0";
    public static T2IRegisteredParam<double> Multiplier;
    public static T2IRegisteredParam<string> PerLayerWeights;

    public const string EditFeatureFlag = "krea2_ostris_edit";
    public const string EditModelPatchNodeName = "Krea2OstrisEditModelPatch";
    public const string EditRepoUrl = "https://github.com/ostris/ComfyUI-Krea2-Ostris-Edit";
    public static T2IRegisteredParam<double> EditReferenceMegapixels;

    public const string InContextFeatureFlag = "krea2_edit";
    public const string InContextModelPatchNodeName = "Krea2EditModelPatch";
    public const string InContextRepoUrl = "https://github.com/lbouaraba/comfyui-krea2edit";
    /// <summary>SwarmUI's own Krea 2 text encoder, retemplated in place to do krea2_edit's grounded encoding.</summary>
    public const string SwarmTextEncodeNodeName = "SwarmClipTextEncodeAdvanced";
    /// <summary>krea2edit's <c>Krea2EditGroundedEncode.DEFAULT_SYSTEM</c>, used when the user leaves the override empty.</summary>
    public const string DefaultGroundingSystemPrompt = "Describe the image by detailing the color, shape, size, texture, quantity, text, spatial relationships of the objects and background:";
    public static T2IRegisteredParam<double> InContextRefBoost;
    public static T2IRegisteredParam<double> InContextRefBoostA;
    public static T2IRegisteredParam<string> InContextFitMode;
    public static T2IRegisteredParam<bool> InContextGroundedEncode;
    public static T2IRegisteredParam<int> InContextGroundingPixels;
    public static T2IRegisteredParam<string> InContextSystemPrompt;

    /// <summary>Where to get the edit LoRAs both editing features need, linked from each edit group's popover help.</summary>
    public const string EditLoraSuggestion = "Use <a target=\"_blank\" href=\"https://huggingface.co/conradlocke/krea2-identity-edit\">conradlocke/krea2-identity-edit</a>"
        + " or <a target=\"_blank\" href=\"https://huggingface.co/silveroxides/SmallHateModels\">silveroxides/SmallHateModels</a>";

    public static T2IParamGroup KreaGoneWildGroup;
    public static T2IParamGroup RebalanceGroup;
    public static T2IParamGroup OstrisEditGroup;
    public static T2IParamGroup InContextEditGroup;
    private const string RefinerSamplerNodeId = "23";
    private const int ReferenceSnap = 16;

    public override void OnInit()
    {
        Logs.Info("SwarmUI Krea Gone Wild Extension initializing...");
        ComfyTyped.Generated.NodeRegistrations.EnsureRegistered();
        Generated.NodeRegistrations.EnsureRegistered();
        ComfyUIBackendExtension.NodeToFeatureMap[RebalanceNodeName] = RebalanceFeatureFlag;
        ComfyUIBackendExtension.NodeToFeatureMap[EditModelPatchNodeName] = EditFeatureFlag;
        ComfyUIBackendExtension.NodeToFeatureMap[InContextModelPatchNodeName] = InContextFeatureFlag;
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Conditioning Rebalance",
            RebalanceFeatureFlag,
            RebalanceRepoUrl,
            "nova452"
        ));
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Ostris Edit",
            EditFeatureFlag,
            EditRepoUrl,
            "ostris"
        ));
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Edit",
            InContextFeatureFlag,
            InContextRepoUrl,
            "lbouaraba"
        ));
        ScriptFiles.Add("assets/krea_gone_wild_install.js");

        KreaGoneWildGroup = new T2IParamGroup(
            Name: "Krea Gone Wild",
            Toggles: false,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 9,
            Description: "Krea 2 tricks: conditioning rebalance and reference-image editing."
        );

        RebalanceGroup = new T2IParamGroup(
            Name: "KGW Rebalance",
            Toggles: true,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 0,
            Parent: KreaGoneWildGroup,
            Description: $"Per-layer conditioning rebalancing for Krea 2, applied to the positive and negative prompts (and the refiner pass, if present).\nToggle this group on to enable it.\n<a target=\"_blank\" href=\"{RebalanceRepoUrl}\">nova452/ComfyUI-ConditioningKrea2Rebalance</a>"
        );

        OstrisEditGroup = new T2IParamGroup(
            Name: "KGW Ostris Edit",
            Toggles: true,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 1,
            Parent: KreaGoneWildGroup,
            Description: $"Reference-image editing that attaches your prompt image(s) as reference latents on the conditioning, plus a model patch so the Krea 2 DiT consumes them.\nNeeds a Krea 2 edit LoRA and at least one prompt image. Toggle this group on to enable it.\n{EditLoraSuggestion}\n<a target=\"_blank\" href=\"{EditRepoUrl}\">ostris/ComfyUI-Krea2-Ostris-Edit</a>"
        );

        InContextEditGroup = new T2IParamGroup(
            Name: "KGW Krea2Edit",
            Toggles: true,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 2,
            Parent: KreaGoneWildGroup,
            Description: $"Instruction-based editing that prepends your prompt image(s) to the sampling sequence as clean in-context tokens, and encodes the prompt through Qwen3-VL together with the image.\nNeeds the krea2_edit identity LoRA and at least one prompt image. Toggle this group on to enable it.\n{EditLoraSuggestion}\n<a target=\"_blank\" href=\"{InContextRepoUrl}\">lbouaraba/comfyui-krea2edit</a>"
        );

        int OrderPriority = 0;

        Multiplier = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Multiplier",
            Description: "Overall multiplier applied to Krea 2 conditioning.",
            Default: "4.00",
            Min: -1, Max: 50, Step: 0.5,
            ViewMin: -1, ViewMax: 50,
            ViewType: ParamViewType.SLIDER,
            Group: RebalanceGroup,
            FeatureFlag: RebalanceFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        PerLayerWeights = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "KGW per_layer_weights",
            Description: "Comma-separated weights for the 12 Krea 2 conditioning layers.",
            Default: DefaultPerLayerWeights,
            Group: RebalanceGroup,
            FeatureFlag: RebalanceFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        EditReferenceMegapixels = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Edit Reference Megapixels",
            Description: "Target size (megapixels) the edit reference image is scaled to before VAE-encoding.",
            Default: "1.00",
            Min: 0.25, Max: 4, Step: 0.05,
            ViewMin: 0.25, ViewMax: 4,
            ViewType: ParamViewType.SLIDER,
            Group: OstrisEditGroup,
            FeatureFlag: EditFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextRefBoost = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Krea2Edit Ref Boost",
            Description: "Reference-fidelity dial: multiplies target-to-reference attention for the LAST reference (the subject, or the only image in a single-reference edit).\n1.0 = off, above 1 pulls harder toward the reference's appearance, below 1 loosens it.",
            Default: "1.00",
            Min: 0, Max: 1000, Step: 0.01,
            ViewMin: 0, ViewMax: 5,
            ViewType: ParamViewType.SLIDER,
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextRefBoostA = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Krea2Edit Ref Boost A",
            Description: "Same dial as Ref Boost, but for the FIRST reference (the scene) in a two-image edit. No effect with a single prompt image.",
            Default: "1.00",
            Min: 0, Max: 1000, Step: 0.01,
            ViewMin: 0, ViewMax: 5,
            ViewType: ParamViewType.SLIDER,
            IsAdvanced: true,
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextFitMode = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "KGW Krea2Edit Fit Mode",
            Description: "How a reference fits an output aspect ratio it doesn't match.\n'fit' resamples the reference onto the target grid at a centered offset (training-matched, use this).\n'crop (legacy)' center-crops to the target aspect ratio then resizes (v1/v1.1 geometry, only for older weights).",
            Default: Krea2EditModelPatchNode.FitModeValues.Fit,
            GetValues: _ => [Krea2EditModelPatchNode.FitModeValues.Fit, Krea2EditModelPatchNode.FitModeValues.CropLegacy],
            IsAdvanced: true,
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextGroundedEncode = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "KGW Krea2Edit Grounded Encode",
            Description: "Retemplate SwarmUI's own Krea 2 text encoder so the vision blocks sit where krea2_edit was trained: before the instruction and unlabelled, instead of SwarmUI's stock \"Picture N:\" suffix after it.\nSwarmUI already grounds Krea 2 on the prompt images, so this only corrects the layout - and because it keeps SwarmClipTextEncodeAdvanced, all the advanced prompt syntax (<break>, [from:to:when], [alter|nate], weighting) keeps working.\nTurn it off to keep SwarmUI's stock krea2 template.",
            Default: "true",
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextGroundingPixels = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "KGW Krea2Edit Grounding Pixels",
            Description: "Caps the longest side of the reference image(s) fed to the Qwen3-VL vision tower during grounded encoding. 0 = leave SwarmUI's own prompt-image sizing alone.\nThe LoRA trained with 384-768px jitter, so 640-768 is in-distribution.",
            Default: "768",
            Min: 0, Max: 4096, Step: 64,
            ViewMin: 0, ViewMax: 1536,
            ViewType: ParamViewType.SLIDER,
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        InContextSystemPrompt = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "KGW Krea2Edit System Prompt",
            Description: "Replaces the system turn of the grounding template. Empty = krea2_edit's training default.\nSteers what the vision encoder attends to, e.g. facial identity detail. Only applies with Grounded Encode on.",
            Default: "",
            ViewType: ParamViewType.PROMPT,
            IsAdvanced: true,
            Group: InContextEditGroup,
            FeatureFlag: InContextFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        WorkflowGenerator.AddStep(DisableReferenceOnlyForKreaEdits, -7.01);
        WorkflowGenerator.AddStep(ApplyKrea2EditGrounding, -6.995);
        WorkflowGenerator.AddStep(ApplyConditioningRebalance, -6.99);
        WorkflowGenerator.AddStep(ApplyKrea2Edit, -5.5);
        WorkflowGenerator.AddStep(ApplyKrea2EditModelPatch, -5.49);
        WorkflowGenerator.AddStep(ApplyRefinerConditioningRebalance, -3.9);
    }

    private static bool IsKrea2(WorkflowGenerator generator)
        => generator.CurrentCompatClass() == T2IModelClassSorter.CompatKrea2.ID;

    /// <summary>Each feature's sub-group carries a group toggle instead of an explicit enable param. SwarmUI submits every
    /// param of a toggled-on group and none of a toggled-off one, so the presence of any one of a group's params is the
    /// group's on/off state. Pass a param that is always registered in the group (not one that can be dropped by IgnoreIf).</summary>
    private static bool IsGroupEnabled<T>(WorkflowGenerator generator, T2IRegisteredParam<T> groupParam)
        => generator.UserInput.TryGet(groupParam, out T _);

    /// <summary>Prevents generic reference conditioning from stacking with the edit packs' reference conditioning.</summary>
    private static void DisableReferenceOnlyForKreaEdits(WorkflowGenerator generator)
    {
        if (!IsKrea2(generator)
            || !generator.UserInput.Get(T2IParamTypes.UseReferenceOnly, false)
            || (!IsGroupEnabled(generator, EditReferenceMegapixels) && !IsGroupEnabled(generator, InContextRefBoost)))
        {
            return;
        }
        generator.UserInput.Set(T2IParamTypes.UseReferenceOnly, false);
        Logs.Warning("Krea Gone Wild: disabled core Use Reference Only because the enabled Krea edit mode provides its own reference conditioning.");
    }

    private static void ApplyConditioningRebalance(WorkflowGenerator generator)
    {
        if (!IsGroupEnabled(generator, Multiplier) || !IsKrea2(generator))
        {
            return;
        }
        double multiplier = generator.UserInput.Get(Multiplier);
        string perLayerWeights = generator.UserInput.Get(PerLayerWeights);
        using WorkflowBridge bridge = BridgeSync.For(generator);
        generator.FinalPrompt = CreateRebalanceNode(bridge, generator.FinalPrompt, multiplier, perLayerWeights);
        generator.FinalNegativePrompt = CreateRebalanceNode(bridge, generator.FinalNegativePrompt, multiplier, perLayerWeights);
    }

    private static void ApplyRefinerConditioningRebalance(WorkflowGenerator generator)
    {
        if (!IsGroupEnabled(generator, Multiplier) || !IsKrea2(generator))
        {
            return;
        }
        if (generator.Workflow?[RefinerSamplerNodeId] is not JObject samplerNode || samplerNode["inputs"] is not JObject inputs)
        {
            return;
        }
        if (inputs["positive"] is not JArray positive || inputs["negative"] is not JArray negative)
        {
            Logs.Debug("Krea Gone Wild: refiner sampler conditioning not in the expected layout; skipping refiner rebalance.");
            return;
        }
        double multiplier = generator.UserInput.Get(Multiplier);
        string perLayerWeights = generator.UserInput.Get(PerLayerWeights);
        JArray positiveNode, negativeNode;
        using (WorkflowBridge bridge = BridgeSync.For(generator))
        {
            positiveNode = CreateRebalanceNode(bridge, (JArray)positive.DeepClone(), multiplier, perLayerWeights);
            negativeNode = CreateRebalanceNode(bridge, (JArray)negative.DeepClone(), multiplier, perLayerWeights);
        }
        inputs["positive"] = positiveNode;
        inputs["negative"] = negativeNode;
    }

    private static void ApplyKrea2Edit(WorkflowGenerator generator)
    {
        if (!IsGroupEnabled(generator, EditReferenceMegapixels) || !IsKrea2(generator))
        {
            return;
        }
        if (generator.CurrentModel is null || generator.CurrentVae is null)
        {
            Logs.Debug("Krea Gone Wild: Krea 2 Edit enabled but model/VAE tracker not ready; skipping.");
            return;
        }
        List<JArray> refImages = [];
        for (int i = 0; ; i++)
        {
            JArray img = generator.GetPromptImage(fixSize: true, promptSize: true, index: i);
            if (img is null)
            {
                break;
            }
            refImages.Add(img);
        }
        if (refImages.Count == 0)
        {
            Logs.Debug("Krea Gone Wild: Krea 2 Edit enabled but no prompt image provided; skipping (no reference to edit).");
            return;
        }
        double megapixels = generator.UserInput.Get(EditReferenceMegapixels, 1.0);
        JArray vae = generator.CurrentVae.Path;
        using WorkflowBridge bridge = BridgeSync.For(generator);
        foreach (JArray img in refImages)
        {
            JArray latent = CreateReferenceLatent(bridge, img, vae, megapixels);
            generator.FinalPrompt = AttachReferenceLatent(bridge, generator.FinalPrompt, latent);
            generator.FinalNegativePrompt = AttachReferenceLatent(bridge, generator.FinalNegativePrompt, latent);
        }
        Krea2OstrisEditModelPatchNode patch = PatchModel(bridge, generator.CurrentModel.Path);
        generator.CurrentModel = patch.MODEL.ToWGNodeData(generator, WGNodeData.DT_MODEL);
    }

    /// <summary>Collects the prompt images the Krea2Edit nodes use as references. Raw (unresized) — the node pack does its own
    /// pixel-space fitting against the target grid, and pre-resizing here would only stack a second resample on top of it.</summary>
    private static List<JArray> GetInContextReferenceImages(WorkflowGenerator generator)
    {
        List<JArray> images = [];
        for (int i = 0; i < 2; i++)
        {
            JArray img = generator.GetPromptImage(fixSize: false, index: i);
            if (img is null)
            {
                break;
            }
            images.Add(img);
        }
        return images;
    }

    /// <summary>Grounds SwarmUI's own text encoders on the prompt image(s) the way krea2_edit was trained, by retemplating
    /// them in place instead of swapping in the pack's <c>Krea2EditGroundedEncode</c>.
    ///
    /// SwarmUI already routes Krea 2 through <c>SwarmClipTextEncodeAdvanced</c> with the images wired and the stock
    /// <c>"krea2"</c> template, which appends the vision blocks *after* the instruction with a "Picture N: " label.
    /// krea2_edit trained with them *before* the instruction and unlabelled. That node passes any non-magic
    /// <c>llama_template</c> string through to <c>clip.tokenize</c> verbatim and leaves the prompt text alone, so handing it
    /// the pack's own template reproduces the pack's grounded encode while keeping <c>&lt;break&gt;</c>, <c>[from:to:when]</c>,
    /// <c>[alter|nate]</c>, weighting and per-step scheduling — all of which the pack's node drops.</summary>
    private static void ApplyKrea2EditGrounding(WorkflowGenerator generator)
    {
        if (!IsGroupEnabled(generator, InContextRefBoost) || !generator.UserInput.Get(InContextGroundedEncode, true) || !IsKrea2(generator))
        {
            return;
        }
        if (!generator.UserInput.TryGet(T2IParamTypes.PromptImages, out List<Image> promptImages) || promptImages.Count == 0)
        {
            Logs.Debug("Krea Gone Wild: Krea2Edit grounding enabled but no prompt image provided; skipping.");
            return;
        }
        // One vision block per image SwarmUI batched into the encoder, or the vision-token count won't match the template.
        string template = BuildGroundingTemplate(promptImages.Count, generator.UserInput.Get(InContextSystemPrompt, ""));
        int groundingPixels = generator.UserInput.Get(InContextGroundingPixels, 768);
        // Both encoders get the same scaled image batch, so the downscale runs once.
        Dictionary<string, JArray> scaledImages = [];
        bool anyPatched = false;
        foreach (JArray conditioning in new[] { generator.FinalPrompt, generator.FinalNegativePrompt })
        {
            if (generator.Workflow?[$"{conditioning?[0]}"] is not JObject node
                || $"{node["class_type"]}" != SwarmTextEncodeNodeName
                || node["inputs"] is not JObject inputs)
            {
                continue;
            }
            inputs["llama_template"] = template;
            if (groundingPixels > 0 && inputs["images"] is JArray images)
            {
                string key = images.ToString(Newtonsoft.Json.Formatting.None);
                if (!scaledImages.TryGetValue(key, out JArray scaled))
                {
                    using WorkflowBridge bridge = BridgeSync.For(generator);
                    // "area" matches the pack's own downscale; unlike the pack this also scales small images *up* to the cap.
                    ImageScaleToMaxDimensionNode scale = bridge.AddNode(new ImageScaleToMaxDimensionNode().With(
                        UpscaleMethod: "area",
                        LargestSize: groundingPixels));
                    scale.Image.ConnectFromPath(bridge, images);
                    scaled = WorkflowBridge.ToPath(scale.IMAGE);
                    scaledImages[key] = scaled;
                }
                inputs["images"] = scaled.DeepClone();
            }
            anyPatched = true;
        }
        if (!anyPatched)
        {
            Logs.Debug($"Krea Gone Wild: Krea2Edit grounding enabled but the conditioning is not a plain {SwarmTextEncodeNodeName}; skipping.");
        }
    }

    /// <summary>Rebuilds krea2edit's <c>Krea2EditGroundedEncode._template</c>: the system turn, then a user turn whose
    /// vision blocks come before the <c>{}</c> the encoder substitutes the instruction into.</summary>
    internal static string BuildGroundingTemplate(int imageCount, string systemPrompt)
    {
        string system = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultGroundingSystemPrompt : systemPrompt.Trim();
        // ComfyUI applies the template as `template.format(text)` (comfy/text_encoders/qwen3vl.py), so a brace typed into
        // the system prompt would blow up inside str.format. Doubling them makes format() emit the literal brace instead.
        system = system.Replace("{", "{{").Replace("}", "}}");
        string vision = string.Concat(Enumerable.Repeat("<|vision_start|><|image_pad|><|vision_end|>", Math.Max(1, imageCount)));
        return $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{vision}{{}}<|im_end|>\n<|im_start|>assistant\n";
    }

    private static void ApplyKrea2EditModelPatch(WorkflowGenerator generator)
    {
        if (!IsGroupEnabled(generator, InContextRefBoost) || !IsKrea2(generator))
        {
            return;
        }
        if (generator.CurrentModel is null || generator.CurrentVae is null)
        {
            Logs.Debug("Krea Gone Wild: Krea2Edit enabled but model/VAE tracker not ready; skipping.");
            return;
        }
        List<JArray> refImages = GetInContextReferenceImages(generator);
        if (refImages.Count == 0)
        {
            Logs.Debug("Krea Gone Wild: Krea2Edit enabled but no prompt image provided; skipping (no reference to edit).");
            return;
        }
        JArray vae = generator.CurrentVae.Path;
        // Wiring target_latent moves the source VAE-encode ahead of sampling, so the VAE can't evict part of the
        // diffusion model mid-run. Null only if nothing has established a latent yet, in which case we just omit it.
        JArray targetLatent = generator.CurrentMedia?.AsLatentImage(generator.CurrentVae)?.Path;
        using WorkflowBridge bridge = BridgeSync.For(generator);
        Krea2EditModelPatchNode patch = bridge.AddNode(new Krea2EditModelPatchNode().With(
            RefBoost: generator.UserInput.Get(InContextRefBoost, 1.0),
            RefBoostA: generator.UserInput.Get(InContextRefBoostA, 1.0),
            FitMode: generator.UserInput.Get(InContextFitMode, Krea2EditModelPatchNode.FitModeValues.Fit)));
        patch.Model.ConnectFromPath(bridge, generator.CurrentModel.Path);
        patch.Vae.ConnectFromPath(bridge, vae);
        // source_latent is required even on the pixel path (where source_image + vae override it), so encode it anyway.
        patch.SourceLatent.ConnectFromPath(bridge, EncodeImage(bridge, refImages[0], vae));
        patch.SourceImage.ConnectFromPath(bridge, refImages[0]);
        if (refImages.Count > 1)
        {
            patch.SourceLatentB.ConnectFromPath(bridge, EncodeImage(bridge, refImages[1], vae));
            patch.SourceImageB.ConnectFromPath(bridge, refImages[1]);
        }
        if (targetLatent is not null)
        {
            patch.TargetLatent.ConnectFromPath(bridge, targetLatent);
        }
        generator.CurrentModel = patch.MODEL.ToWGNodeData(generator, WGNodeData.DT_MODEL);
    }

    internal static JArray EncodeImage(WorkflowBridge bridge, JArray image, JArray vae)
    {
        VAEEncodeNode encode = bridge.AddNode(new VAEEncodeNode());
        encode.Pixels.ConnectFromPath(bridge, image);
        encode.Vae.ConnectFromPath(bridge, vae);
        return WorkflowBridge.ToPath(encode.LATENT);
    }

    internal static JArray CreateRebalanceNode(WorkflowBridge bridge, JArray conditioning, double multiplier, string perLayerWeights)
    {
        ConditioningKrea2RebalanceNode node = bridge.AddNode(new ConditioningKrea2RebalanceNode().With(
            Multiplier: multiplier,
            PerLayerWeights: perLayerWeights));
        node.ConditioningInput.ConnectFromPath(bridge, conditioning);
        return WorkflowBridge.ToPath(node.Conditioning);
    }

    internal static JArray CreateReferenceLatent(WorkflowBridge bridge, JArray image, JArray vae, double megapixels)
    {
        ImageScaleToTotalPixelsNode scale = bridge.AddNode(new ImageScaleToTotalPixelsNode().With(
            UpscaleMethod: "area",
            Megapixels: megapixels,
            ResolutionSteps: ReferenceSnap));
        scale.Image.ConnectFromPath(bridge, image);
        VAEEncodeNode encode = bridge.AddNode(new VAEEncodeNode());
        encode.Pixels.ConnectTo(scale.IMAGE);
        encode.Vae.ConnectFromPath(bridge, vae);
        return WorkflowBridge.ToPath(encode.LATENT);
    }

    internal static JArray AttachReferenceLatent(WorkflowBridge bridge, JArray conditioning, JArray latent)
    {
        ReferenceLatentNode node = bridge.AddNode(new ReferenceLatentNode());
        node.Conditioning.ConnectFromPath(bridge, conditioning);
        node.Latent.ConnectFromPath(bridge, latent);
        return WorkflowBridge.ToPath(node.CONDITIONING);
    }

    internal static Krea2OstrisEditModelPatchNode PatchModel(WorkflowBridge bridge, JArray model)
    {
        Krea2OstrisEditModelPatchNode patch = bridge.AddNode(new Krea2OstrisEditModelPatchNode());
        patch.Model.ConnectFromPath(bridge, model);
        return patch;
    }
}
