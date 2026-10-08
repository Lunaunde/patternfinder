shader_type canvas_item;
render_mode unshaded;

// Clear only the example motif baked into the existing hand-drawn artwork.
varying vec4 item_color;
void vertex() { item_color = COLOR; }
void fragment() {
    vec4 paper = texture(TEXTURE, UV);
    vec2 pixel = UV * vec2(183.0, 261.0);
    if (pixel.x >= 48.0 && pixel.x <= 125.0 && pixel.y >= 48.0 && pixel.y <= 159.0) {
        paper.rgb = vec3(1.0, 253.0 / 255.0, 1.0);
    }
    COLOR = paper * item_color;
}
