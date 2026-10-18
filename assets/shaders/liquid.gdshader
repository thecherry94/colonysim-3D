shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;

#include "res://assets/shaders/block_common.gdshaderinc"

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (above_slice(world_pos)) discard;
	ALBEDO = block_albedo(UV, UV2);
	ALPHA = 0.7;
	ROUGHNESS = 0.3;
}
