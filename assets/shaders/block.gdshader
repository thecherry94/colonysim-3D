shader_type spatial;

#include "res://assets/shaders/block_common.gdshaderinc"

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (above_slice(world_pos)) discard;
	ALBEDO = block_albedo(UV, UV2);
	ROUGHNESS = 1.0;
	SPECULAR = 0.2;
}
