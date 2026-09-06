using ComfyTyped.Core;
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

    public static T2IParamGroup KreaGoneWildGroup;
    public static T2IParamGroup RebalanceGroup;
    private const string RefinerSamplerNodeId = "23";

    public override void OnInit()
    {
        Logs.Info("SwarmUI Krea Gone Wild Extension initializing...");
        ComfyTyped.Generated.NodeRegistrations.EnsureRegistered();
        Generated.NodeRegistrations.EnsureRegistered();
        ComfyUIBackendExtension.NodeToFeatureMap[RebalanceNodeName] = RebalanceFeatureFlag;
        InstallableFeatures.RegisterInstallableFeature(new(
            "Krea 2 Conditioning Rebalance",
            RebalanceFeatureFlag,
            RebalanceRepoUrl,
            "nova452"
        ));
        ScriptFiles.Add("assets/krea_gone_wild_install.js");

        KreaGoneWildGroup = new T2IParamGroup(
            Name: "Krea Gone Wild",
            Toggles: false,
            Open: false,
            IsAdvanced: false,
            OrderPriority: 9,
            Description: "Per-layer conditioning rebalancing for Krea 2."
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

        WorkflowGenerator.AddStep(ApplyConditioningRebalance, -6.99);
        WorkflowGenerator.AddStep(ApplyRefinerConditioningRebalance, -3.9);
    }

    private static bool IsKrea2(WorkflowGenerator generator)
        => generator.CurrentCompatClass() == T2IModelClassSorter.CompatKrea2.ID;

    /// <summary>Each feature's sub-group carries a group toggle instead of an explicit enable param. SwarmUI submits every
    /// param of a toggled-on group and none of a toggled-off one, so the presence of any one of a group's params is the
    /// group's on/off state. Pass a param that is always registered in the group (not one that can be dropped by IgnoreIf).</summary>
    private static bool IsGroupEnabled<T>(WorkflowGenerator generator, T2IRegisteredParam<T> groupParam)
        => generator.UserInput.TryGet(groupParam, out T _);

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

    internal static JArray CreateRebalanceNode(WorkflowBridge bridge, JArray conditioning, double multiplier, string perLayerWeights)
    {
        ConditioningKrea2RebalanceNode node = bridge.AddNode(new ConditioningKrea2RebalanceNode().With(
            Multiplier: multiplier,
            PerLayerWeights: perLayerWeights));
        node.ConditioningInput.ConnectFromPath(bridge, conditioning);
        return WorkflowBridge.ToPath(node.Conditioning);
    }
}
