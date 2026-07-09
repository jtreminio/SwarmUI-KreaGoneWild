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
    public const string DefaultPerLayerWeights = "1.0,1.0,1.0,1.0,1.0,1.0,1.0,2.5,5.0,1.1,4.0,1.0";
    public static T2IRegisteredParam<bool> Enable;
    public static T2IRegisteredParam<double> Multiplier;
    public static T2IRegisteredParam<string> PerLayerWeights;

    public const string EditFeatureFlag = "krea2_ostris_edit";
    public const string EditModelPatchNodeName = "Krea2OstrisEditModelPatch";
    public static T2IRegisteredParam<bool> EditEnable;
    public static T2IRegisteredParam<double> EditReferenceMegapixels;

    public static T2IParamGroup KreaGoneWildGroup;
    private const string RefinerSamplerNodeId = "23";
    private const int ReferenceSnap = 16;

    public override void OnInit()
    {
        Logs.Info("SwarmUI Krea Gone Wild Extension initializing...");
        ComfyTyped.Generated.NodeRegistrations.EnsureRegistered();
        Generated.NodeRegistrations.EnsureRegistered();
        ComfyUIBackendExtension.NodeToFeatureMap[RebalanceNodeName] = RebalanceFeatureFlag;
        ComfyUIBackendExtension.NodeToFeatureMap[EditModelPatchNodeName] = EditFeatureFlag;
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Conditioning Rebalance",
            RebalanceFeatureFlag,
            "https://github.com/nova452/ComfyUI-ConditioningKrea2Rebalance",
            "nova452"
        ));
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Ostris Edit",
            EditFeatureFlag,
            "https://github.com/ostris/ComfyUI-Krea2-Ostris-Edit",
            "ostris"
        ));
        ScriptFiles.Add("assets/krea_gone_wild_install.js");

        KreaGoneWildGroup = new T2IParamGroup(
            Name: "Krea Gone Wild",
            Toggles: true,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 9,
            Description: "Krea 2 tricks: conditioning rebalance and reference-image editing."
        );

        int OrderPriority = 0;

        Enable = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "KGW Enable",
            Description: "Apply Krea 2 conditioning rebalancing to both the positive and negative prompts.",
            Default: "false",
            IgnoreIf: "false",
            Group: KreaGoneWildGroup,
            FeatureFlag: RebalanceFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        Multiplier = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Multiplier",
            Description: "Overall multiplier applied to Krea 2 conditioning.",
            Default: "4.00",
            Min: -1, Max: 50, Step: 0.5,
            ViewMin: -1, ViewMax: 50,
            ViewType: ParamViewType.SLIDER,
            Group: KreaGoneWildGroup,
            FeatureFlag: RebalanceFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        PerLayerWeights = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "KGW per_layer_weights",
            Description: "Comma-separated weights for the 12 Krea 2 conditioning layers.",
            Default: DefaultPerLayerWeights,
            Group: KreaGoneWildGroup,
            FeatureFlag: RebalanceFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        EditEnable = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "KGW Krea 2 Edit",
            Description: "Enable Krea 2 reference-image editing (Ostris edit LoRAs).",
            Default: "false",
            IgnoreIf: "false",
            Group: KreaGoneWildGroup,
            FeatureFlag: EditFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        EditReferenceMegapixels = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Edit Reference Megapixels",
            Description: "Target size (megapixels) the edit reference image is scaled to before VAE-encoding.",
            Default: "1.00",
            Min: 0.25, Max: 4, Step: 0.05,
            ViewMin: 0.25, ViewMax: 4,
            ViewType: ParamViewType.SLIDER,
            IsAdvanced: true,
            Group: KreaGoneWildGroup,
            FeatureFlag: EditFeatureFlag,
            OrderPriority: OrderPriority++
        ));

        WorkflowGenerator.AddStep(ApplyConditioningRebalance, -6.99);
        WorkflowGenerator.AddStep(ApplyKrea2Edit, -5.5);
        WorkflowGenerator.AddStep(ApplyRefinerConditioningRebalance, -3.9);
    }

    private static bool IsKrea2(WorkflowGenerator generator)
        => generator.CurrentCompatClass() == T2IModelClassSorter.CompatKrea2.ID;

    private static void ApplyConditioningRebalance(WorkflowGenerator generator)
    {
        if (!generator.UserInput.Get(Enable, false) || !IsKrea2(generator))
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
        if (!generator.UserInput.Get(Enable, false) || !IsKrea2(generator))
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
        if (!generator.UserInput.Get(EditEnable, false) || !IsKrea2(generator))
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
