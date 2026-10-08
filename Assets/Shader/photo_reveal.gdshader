shader_type canvas_item;
render_mode unshaded;

uniform sampler2D photo_frame_texture : source_color, filter_linear, repeat_disable;
uniform vec4 photo_frame_uv_rect;
uniform vec4 photo_frame_rect;
uniform vec4 photo_window_rect;
uniform vec4 photo_image_rect;
uniform sampler2D material_frame_texture : source_color, filter_linear, repeat_disable;
uniform vec4 material_frame_rect;
uniform sampler2D material_texture : source_color, filter_linear, repeat_disable;
uniform vec2 material_uv_extent = vec2(1.0);
uniform vec4 material_uv_rect = vec4(0.0, 0.0, 1.0, 1.0);
uniform bool recognition_success = true;
uniform float reveal_radius : hint_range(0.0, 1.2) = 0.0;
uniform float edge_softness = 0.008;
varying vec4 item_color;

void vertex() { item_color = COLOR; }

float inside(vec2 uv) {
    return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
}

vec2 local_uv(vec2 uv, vec4 rect) { return (uv - rect.xy) / rect.zw; }

vec4 over(vec4 background, vec4 foreground) {
    float alpha = foreground.a + background.a * (1.0 - foreground.a);
    vec3 rgb = (foreground.rgb * foreground.a + background.rgb * background.a
        * (1.0 - foreground.a)) / max(alpha, 0.00001);
    return vec4(rgb, alpha);
}

vec4 sample_pattern(vec2 uv) {
    vec2 atlas_uv = material_uv_rect.xy + clamp(uv, vec2(0.0), vec2(1.0)) * material_uv_rect.zw;
    vec4 pattern = texture(material_texture, atlas_uv);
    pattern.a *= inside(uv);
    return pattern;
}

void fragment() {
    vec2 frame_uv = local_uv(UV, photo_frame_rect);
    vec4 photograph = texture(photo_frame_texture, photo_frame_uv_rect.xy
        + clamp(frame_uv, vec2(0.0), vec2(1.0)) * photo_frame_uv_rect.zw);
    photograph.a *= inside(frame_uv);
    vec2 photo_uv = local_uv(UV, photo_image_rect);
    photograph = over(photograph, vec4(vec3(1.0), inside(local_uv(UV, photo_window_rect))));
    vec4 snapshot = texture(TEXTURE, clamp(photo_uv, vec2(0.0), vec2(1.0)));
    snapshot.a *= inside(photo_uv);
    photograph = over(photograph, snapshot);

    vec4 target;
    if (recognition_success) {
        vec2 card_uv = local_uv(UV, material_frame_rect);
        vec2 pixel = vec2(11.0, 23.0) + clamp(card_uv, vec2(0.0), vec2(1.0)) * vec2(161.0, 210.0);
        target = texture(material_frame_texture, pixel / vec2(183.0, 261.0));
        target.a *= inside(card_uv);
        if (pixel.x >= 48.0 && pixel.x <= 125.0 && pixel.y >= 48.0 && pixel.y <= 159.0) {
            target.rgb = vec3(1.0, 253.0 / 255.0, 1.0);
        }
        vec2 pattern_uv = ((pixel - vec2(28.0, 39.0)) / vec2(127.0, 115.0) - vec2(0.5))
            / material_uv_extent + vec2(0.5);
        vec4 pattern = sample_pattern(pattern_uv);
        pattern.a *= inside(card_uv);
        target = over(target, pattern);
    } else {
        target = texture(photo_frame_texture, photo_frame_uv_rect.xy
            + clamp(frame_uv, vec2(0.0), vec2(1.0)) * photo_frame_uv_rect.zw);
        target.a *= inside(frame_uv);
        target = over(target, vec4(vec3(1.0), inside(local_uv(UV, photo_window_rect))));
        vec2 failure_uv = (photo_uv - vec2(0.5)) / vec2(0.8) + vec2(0.5);
        target = over(target, sample_pattern(failure_uv));
    }

    // Pixel distance makes the reveal circular even on a rectangular card.
    vec2 center = photo_image_rect.xy + photo_image_rect.zw * 0.5;
    float distance_to_center = length((UV - center) / photo_image_rect.zw);
    float revealed = reveal_radius > 0.0
        ? 1.0 - smoothstep(reveal_radius - edge_softness, reveal_radius + edge_softness, distance_to_center)
        : 0.0;
    float alpha = mix(photograph.a, target.a, revealed);
    vec3 rgb = mix(photograph.rgb * photograph.a, target.rgb * target.a, revealed) / max(alpha, 0.00001);
    COLOR = vec4(rgb, alpha) * item_color;
}
