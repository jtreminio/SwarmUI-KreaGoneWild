using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace KreaGoneWild;

/// <summary>Rebalances the positive and negative conditioning produced for Krea 2 models.</summary>
public class KreaGoneWildExtension : Extension
{
    /// <summary>Feature flag reported when the required ComfyUI node is available.</summary>
    public const string FeatureFlag = "conditioning_krea2_rebalance";

    /// <summary>ComfyUI class name provided by ComfyUI-ConditioningKrea2Rebalance.</summary>
    public const string NodeName = "ConditioningKrea2Rebalance";

    /// <summary>Default per-layer gains recommended by the ComfyUI node.</summary>
    public const string DefaultPerLayerWeights = "1.0,1.0,1.0,1.0,1.0,1.0,1.0,2.5,5.0,1.1,4.0,1.0";

    /// <summary>Whether conditioning rebalancing is enabled.</summary>
    public static T2IRegisteredParam<bool> Enable;

    /// <summary>Overall conditioning multiplier.</summary>
    public static T2IRegisteredParam<double> Multiplier;

    /// <summary>Comma-separated gain for each Krea 2 conditioning layer.</summary>
    public static T2IRegisteredParam<string> PerLayerWeights;

    /// <summary>Parameter group shown on the generation page.</summary>
    public static T2IParamGroup KreaGoneWildGroup;

    /// <inheritdoc/>
    public override void OnInit()
    {
        Logs.Info("SwarmUI Krea Gone Wild Extension initializing...");

        ComfyUIBackendExtension.NodeToFeatureMap[NodeName] = FeatureFlag;
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Conditioning Rebalance",
            FeatureFlag,
            "https://github.com/nova452/ComfyUI-ConditioningKrea2Rebalance",
            "nova452"
        ));
        ScriptFiles.Add("assets/krea_gone_wild_install.js");

        KreaGoneWildGroup = new T2IParamGroup(
            Name: "Krea Gone Wild",
            Toggles: false,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 9
        );

        Enable = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Enable",
            Description: "Apply Krea 2 conditioning rebalancing to both the positive and negative prompts.",
            Default: "false",
            IgnoreIf: "false",
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 0,
            ID: "kreagonewildenable"
        ));

        Multiplier = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "Multiplier",
            Description: "Overall multiplier applied to Krea 2 conditioning.",
            Default: "4.00",
            Min: -1, Max: 50, Step: 0.5,
            ViewMin: -1, ViewMax: 50,
            ViewType: ParamViewType.SLIDER,
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 1,
            ID: "kreagonewildmultiplier"
        ));

        PerLayerWeights = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "per_layer_weights",
            Description: "Comma-separated weights for the 12 Krea 2 conditioning layers.",
            Default: DefaultPerLayerWeights,
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 2,
            ID: "kreagonewildperlayerweights"
        ));

        // Base stage: rebalance the conditioning before the main sampler (-5) consumes it.
        WorkflowGenerator.AddStep(ApplyConditioningRebalance, -6.99);
        // Refiner stage: the refiner builds fresh conditioning of its own (it does not reuse
        // FinalPrompt), so rebalance its sampler's conditioning after the refiner step (-4) runs.
        WorkflowGenerator.AddStep(ApplyRefinerConditioningRebalance, -3.9);
    }

    /// <summary>Adds a rebalance node after each of the positive and negative prompt encoders.</summary>
    private static void ApplyConditioningRebalance(WorkflowGenerator generator)
    {
        if (!generator.UserInput.Get(Enable, false) || generator.CurrentCompatClass() != T2IModelClassSorter.CompatKrea2.ID)
        {
            return;
        }

        double multiplier = generator.UserInput.Get(Multiplier);
        string perLayerWeights = generator.UserInput.Get(PerLayerWeights);
        string positiveNode = CreateRebalanceNode(generator, generator.FinalPrompt, multiplier, perLayerWeights);
        string negativeNode = CreateRebalanceNode(generator, generator.FinalNegativePrompt, multiplier, perLayerWeights);
        generator.FinalPrompt = [positiveNode, 0];
        generator.FinalNegativePrompt = [negativeNode, 0];
    }

    /// <summary>ComfyUI node ID SwarmUI assigns to the refiner stage KSampler.</summary>
    private const string RefinerSamplerNodeId = "23";

    /// <summary>Rebalances the conditioning fed into the refiner stage sampler, if a Krea 2 refiner ran.</summary>
    private static void ApplyRefinerConditioningRebalance(WorkflowGenerator generator)
    {
        if (!generator.UserInput.Get(Enable, false) || generator.CurrentCompatClass() != T2IModelClassSorter.CompatKrea2.ID)
        {
            return;
        }
        // CurrentCompatClass now reflects the refiner model (the refiner step sets FinalLoadedModel),
        // so reaching here means the refiner stage itself uses Krea 2 conditioning.
        if (generator.Workflow?[RefinerSamplerNodeId] is not JObject samplerNode || samplerNode["inputs"] is not JObject inputs)
        {
            // No refiner sampler in this workflow (no refiner used, or it returned early) - nothing to do.
            return;
        }
        if (inputs["positive"] is not JArray positive || inputs["negative"] is not JArray negative)
        {
            // Refiner sampler uses a non-standard conditioning layout (e.g. an init-image guider chain),
            // so the plain conditioning inputs aren't present to wrap.
            Logs.Debug("Krea Gone Wild: refiner sampler conditioning not in the expected layout; skipping refiner rebalance.");
            return;
        }
        double multiplier = generator.UserInput.Get(Multiplier);
        string perLayerWeights = generator.UserInput.Get(PerLayerWeights);
        // DeepClone so the existing conditioning links can be reused as inputs to the new rebalance nodes.
        string positiveNode = CreateRebalanceNode(generator, (JArray)positive.DeepClone(), multiplier, perLayerWeights);
        string negativeNode = CreateRebalanceNode(generator, (JArray)negative.DeepClone(), multiplier, perLayerWeights);
        inputs["positive"] = new JArray { positiveNode, 0 };
        inputs["negative"] = new JArray { negativeNode, 0 };
    }

    /// <summary>Creates one conditioning rebalance node for a conditioning path.</summary>
    private static string CreateRebalanceNode(WorkflowGenerator generator, JArray conditioning, double multiplier, string perLayerWeights)
    {
        return generator.CreateNode(NodeName, new JObject
        {
            ["conditioning"] = conditioning,
            ["multiplier"] = multiplier,
            ["per_layer_weights"] = perLayerWeights
        });
    }
}
