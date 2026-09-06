# SwarmUI-KreaGoneWild

A SwarmUI extension for Krea 2 conditioning rebalance. It is gated on the selected model being
detected as **Krea 2** (`krea-2` / `krea-2/lora`) and does nothing otherwise.

## KGW Rebalance

Applies [ComfyUI-ConditioningKrea2Rebalance](https://github.com/nova452/ComfyUI-ConditioningKrea2Rebalance)
to both the positive and negative Krea 2 conditioning (and the refiner pass, if present).

- Toggle the **KGW Rebalance** group under **Krea Gone Wild** on to enable it.
- **KGW Multiplier** — overall conditioning multiplier.
- **KGW per_layer_weights** — comma-separated weights for the 12 Krea 2 conditioning layers.

The group's toggle is the on/off switch; there is no separate enable parameter. An install button
appears inside the group when the node pack is missing.

## Usage

1. Install the ComfyUI node using the button in **KGW Rebalance**, then restart the ComfyUI backend
   when prompted.
2. Select a Krea 2 model.
3. Toggle on **KGW Rebalance** and adjust the multiplier and per-layer weights.
