shader_type spatial;

#include "res://assets/shaders/block_common.gdshaderinc"

// Slice caps: the block shader, darkened so cut-open rock reads as solid mass (0 = raw block colors).
// Caps are always rebuilt at the current cut, so they need no slice discard.
uniform float darken = 0.68;

// Caps lie exactly on the cut plane, where the chunk mesh may also draw the cut block's top face (when the
// block above is stairs: solid but not occluding). Biasing the cap's depth slightly toward the camera makes it
// win without z-fighting. It's a clip-space bias, so the cap doesn't move on screen and opens no gaps at its
// edges. Forward+ uses reverse Z: a larger depth is nearer.
const float CAP_DEPTH_BIAS = 1e-5;

void vertex() {
	POSITION = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	POSITION.z *= 1.0 + CAP_DEPTH_BIAS;
}

void fragment() {
	vec3 albedo = block_albedo(UV, UV2);
	ALBEDO = mix(albedo, vec3(0.078, 0.090, 0.114), darken);
	ROUGHNESS = 1.0;
	SPECULAR = 0.2;
}
