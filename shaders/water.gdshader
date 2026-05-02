shader_type canvas_item;

uniform vec4 deep_color : source_color = vec4(0.08, 0.22, 0.45, 1.0);
uniform vec4 shallow_color : source_color = vec4(0.25, 0.55, 0.80, 1.0);
uniform vec4 highlight_color : source_color = vec4(0.80, 0.92, 1.00, 1.0);
uniform float scroll_speed = 0.6;
uniform float noise_scale = 0.025;
// Large-scale brightness variation, keyed off world UV. Anchored in
// world space (no time scrolling) so it reads as static currents/depth
// patches rather than moving texture — breaks the uniformity that made
// the island look like it was floating.
uniform float macro_scale = 0.003;
uniform float macro_amplitude = 0.35;
uniform float u_time = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++) {
        v += a * vnoise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

void fragment() {
    vec2 p = UV * noise_scale;
    // Bounded wobble: each noise sample point oscillates in an ellipse
    // instead of translating. The pattern shimmers/breathes in place
    // with no net direction, so it doesn't read as water flowing under
    // the island. Two layers wobble at different freqs/phases to avoid
    // a synchronized pulse.
    float t = u_time * scroll_speed;
    vec2 wobble1 = vec2(sin(t * 0.55), cos(t * 0.42)) * 1.1;
    vec2 wobble2 = vec2(cos(t * 0.71 + 1.3), sin(t * 0.63)) * 0.9;
    float n = 0.5 * fbm(p + wobble1) + 0.5 * fbm(p * 1.7 + wobble2);
    vec3 base = mix(deep_color.rgb, shallow_color.rgb, smoothstep(0.35, 0.75, n));
    float spec = smoothstep(0.78, 0.92, n);
    vec3 col = mix(base, highlight_color.rgb, spec * 0.6);

    // World-space slow noise → multiplicative brightness factor in
    // [1 - amp, 1 + amp]. Different ocean regions read as visibly
    // distinct deep/shallow patches, breaking the uniform-texture look.
    float macro = fbm(UV * macro_scale);
    col *= mix(1.0 - macro_amplitude, 1.0 + macro_amplitude, macro);
    COLOR = vec4(col, 1.0);
}
