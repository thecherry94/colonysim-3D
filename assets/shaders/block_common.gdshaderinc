// Shared by every block-world shader: the global slice cut and block texture sampling.
global uniform float slice_height;

// Repeats so merged slice-cap quads can tile one texture per block.
uniform sampler2DArray blocks : source_color, filter_nearest_mipmap, repeat_enable;

varying vec3 world_pos;

// UV2.x = texture layer, UV2.y = ambient occlusion (1 = unoccluded).
vec3 block_albedo(vec2 uv, vec2 uv2) {
	return texture(blocks, vec3(uv, uv2.x)).rgb * mix(0.55, 1.0, uv2.y);
}

bool above_slice(vec3 p) {
	return p.y > slice_height + 0.001;
}
