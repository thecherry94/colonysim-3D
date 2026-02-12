shader_type spatial;

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

    vec3 color = COLOR.rgb;

    // Cross-section tint: darken upward-facing surfaces near the slice level
    // to show a clear "floor plan" of the cut. Only affects top faces (NORMAL.y > 0.5),
    // not side walls — this prevents the ugly dark blob effect on walls.
    if (slice_enabled && NORMAL.y > 0.5) {
        float dist_below = slice_y_level - world_position.y;
        if (dist_below >= 0.0 && dist_below < 1.0) {
            color = color * 0.55;
        }
    }

    ALBEDO = color;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
