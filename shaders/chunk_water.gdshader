shader_type spatial;
render_mode blend_mix, depth_draw_opaque;

global uniform float slice_y_level;
global uniform bool slice_enabled;

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    if (slice_enabled && world_position.y > slice_y_level + 0.01) {
        discard;
    }
    ALBEDO = COLOR.rgb;
    ALPHA = COLOR.a;
    ROUGHNESS = 0.2;
    METALLIC = 0.1;
}
