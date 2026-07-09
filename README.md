# SwarmUI-KreaGoneWild

A SwarmUI extension with a small toolbox of Krea 2 tricks. Everything is gated on the selected
model being detected as **Krea 2** (`krea-2` / `krea-2/lora`) and does nothing otherwise.

Two features, each backed by a third-party ComfyUI node pack you install from the **Krea Gone Wild**
parameter group:

## 1. Conditioning Rebalance

Applies [ComfyUI-ConditioningKrea2Rebalance](https://github.com/nova452/ComfyUI-ConditioningKrea2Rebalance)
to both the positive and negative Krea 2 conditioning (and the refiner pass, if present).

- **KGW Enable** — turn it on.
- **KGW Multiplier** — overall conditioning multiplier.
- **KGW per_layer_weights** — comma-separated weights for the 12 Krea 2 conditioning layers.

## 2. Krea 2 Reference-Image Edit

Runs [ComfyUI-Krea2-Ostris-Edit](https://github.com/ostris/ComfyUI-Krea2-Ostris-Edit) edit LoRAs
(ai-toolkit `krea2` arch, `model_kwargs.edit: true`) — "Kontext-style" reference-image editing.

It wires the edit natively into SwarmUI rather than replacing SwarmUI's conditioning:

1. Your prompt image(s) are scaled to ~1 MP (÷16 snapped, matching the training preprocessing) and
   VAE-encoded, then attached as **reference latents** onto the positive and negative conditioning via
   the core `ReferenceLatent` node.
2. The model is wrapped in `Krea2OstrisEditModelPatch` so the Krea 2 DiT actually consumes those
   reference latents (`index_timestep_zero`).

Because SwarmUI's own `SwarmClipTextEncodeAdvanced` still builds the conditioning, all the advanced
prompt syntax (`<break>`, weighting, `[from:to:when]`, regions, …) keeps working on edits. Only the
model patch comes from the ostris pack; the reference latents use core ComfyUI nodes — exactly the
"chain conditioning from … Set Reference Latent nodes" path the pack documents.

- **KGW Krea 2 Edit** — turn it on. Requires a Krea 2 **edit LoRA** loaded and at least one prompt
  image. With no prompt image it is a no-op (the model patch passes through unchanged).
- **KGW Edit Reference Megapixels** *(advanced)* — target size the reference is scaled to before
  VAE-encoding (default 1.0, matching ai-toolkit).

> Note: the reference latents are attached to the main generation pass only; a separate refiner pass
> is not edit-patched.

## Usage

The **Krea Gone Wild** parameter group has a master toggle. When it is off, the whole extension is
off for that generation (SwarmUI omits every param in the group). Turn it on, then enable the
individual feature(s) you want.

1. Install the required ComfyUI node(s) using the button(s) in the **Krea Gone Wild** parameter group,
   then restart the ComfyUI backend when prompted.
2. Select a Krea 2 model.
3. Toggle the **Krea Gone Wild** group on, then enable the feature(s) you want (and, for editing, add a
   prompt/init image + a Krea 2 edit LoRA).
