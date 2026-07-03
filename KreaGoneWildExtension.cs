using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace KreaGoneWild;

public class KreaGoneWildExtension : Extension
{
    public const string FeatureFlag = "conditioning_krea2_rebalance";
    public const string NodeName = "ConditioningKrea2Rebalance";
    public const string DefaultPerLayerWeights = "1.0,1.0,1.0,1.0,1.0,1.0,1.0,2.5,5.0,1.1,4.0,1.0";
    public static T2IRegisteredParam<bool> Enable;
    public static T2IRegisteredParam<double> Multiplier;
    public static T2IRegisteredParam<string> PerLayerWeights;
    public static T2IParamGroup KreaGoneWildGroup;
    private const string RefinerSamplerNodeId = "23";

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
            Name: "KGW Enable",
            Description: "Apply Krea 2 conditioning rebalancing to both the positive and negative prompts.",
            Default: "false",
            IgnoreIf: "false",
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 0
        ));

        Multiplier = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "KGW Multiplier",
            Description: "Overall multiplier applied to Krea 2 conditioning.",
            Default: "4.00",
            Min: -1, Max: 50, Step: 0.5,
            ViewMin: -1, ViewMax: 50,
            ViewType: ParamViewType.SLIDER,
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 1
        ));

        PerLayerWeights = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "KGW per_layer_weights",
            Description: "Comma-separated weights for the 12 Krea 2 conditioning layers.",
            Default: DefaultPerLayerWeights,
            Group: KreaGoneWildGroup,
            FeatureFlag: FeatureFlag,
            OrderPriority: 2
        ));

        WorkflowGenerator.AddStep(ApplyConditioningRebalance, -6.99);
        WorkflowGenerator.AddStep(ApplyRefinerConditioningRebalance, -3.9);
    }

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

    private static void ApplyRefinerConditioningRebalance(WorkflowGenerator generator)
    {
        if (!generator.UserInput.Get(Enable, false) || generator.CurrentCompatClass() != T2IModelClassSorter.CompatKrea2.ID)
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
        string positiveNode = CreateRebalanceNode(generator, (JArray)positive.DeepClone(), multiplier, perLayerWeights);
        string negativeNode = CreateRebalanceNode(generator, (JArray)negative.DeepClone(), multiplier, perLayerWeights);
        inputs["positive"] = new JArray { positiveNode, 0 };
        inputs["negative"] = new JArray { negativeNode, 0 };
    }

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
