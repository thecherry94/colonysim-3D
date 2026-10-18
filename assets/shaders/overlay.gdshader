shader_type spatial;
render_mode unshaded, depth_draw_never, cull_back, shadows_disabled;

// Translucent job and drag-preview cubes, one MultiMesh instance per cell.
global uniform float slice_height;

uniform vec4 color : source_color = vec4(1.0, 0.824, 0.247, 0.45);

// Center height of the cell this instance marks (the multimesh transform is part of MODEL_MATRIX).
varying flat float cell_center_y;

void vertex() {
	cell_center_y = MODEL_MATRIX[3].y;
}

void fragment() {
	// The slice discard, per cell: the cubes are a little larger than a block, so a per-fragment test would clip the
	// top of every cube on the view level and leave its marker invisible on the cut.
	if (cell_center_y > slice_height) discard;
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
