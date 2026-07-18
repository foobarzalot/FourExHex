// FourExHex sound-design engine — deterministic Web Audio synthesis.
// Three palettes x 18 cues. Every render is normalized to -3 dBFS peak so
// AudioBus VolumeDb stays the only gain knob (same convention as the repo's
// generate_capture_sfx.py). window.SfxEngine is the public surface.
(function () {
  var SR = 44100;

  // ---- toolkit bound to an (Offline)AudioContext ----
  function tk(ctx) {
    var dest = ctx.destination;
    function envGain(t0, peak, att, dur) {
      var g = ctx.createGain();
      g.gain.setValueAtTime(1e-4, t0);
      g.gain.exponentialRampToValueAtTime(Math.max(peak, 1e-4), t0 + att);
      g.gain.exponentialRampToValueAtTime(1e-4, t0 + dur);
      g.connect(dest);
      return g;
    }
    function T(o) { // tone: {f, f1?, type?, t0?, dur, peak, att?}
      var t0 = o.t0 || 0, att = o.att || 0.006;
      var osc = ctx.createOscillator();
      osc.type = o.type || 'sine';
      osc.frequency.setValueAtTime(o.f, t0);
      if (o.f1) osc.frequency.exponentialRampToValueAtTime(o.f1, t0 + o.dur);
      osc.connect(envGain(t0, o.peak, att, o.dur));
      osc.start(t0); osc.stop(t0 + o.dur + 0.02);
    }
    function Nz(o) { // noise: {t0?, dur, peak, att?, lp?, lp1?, hp?, q?}
      var t0 = o.t0 || 0, att = o.att || 0.002;
      var n = Math.ceil(o.dur * SR) + 8;
      var b = ctx.createBuffer(1, n, SR), d = b.getChannelData(0);
      for (var i = 0; i < n; i++) d[i] = Math.random() * 2 - 1;
      var s = ctx.createBufferSource(); s.buffer = b;
      var node = s;
      if (o.lp) {
        var f = ctx.createBiquadFilter(); f.type = 'lowpass';
        f.frequency.setValueAtTime(o.lp, t0);
        if (o.lp1) f.frequency.exponentialRampToValueAtTime(o.lp1, t0 + o.dur);
        f.Q.value = o.q || 0.8; node.connect(f); node = f;
      }
      if (o.hp) {
        var h = ctx.createBiquadFilter(); h.type = 'highpass';
        h.frequency.value = o.hp; h.Q.value = o.q || 0.8;
        node.connect(h); node = h;
      }
      node.connect(envGain(t0, o.peak, att, o.dur));
      s.start(t0);
    }
    function B(o) { // bell/gong: {f, t0?, dur, peak, att?, partials?}
      var t0 = o.t0 || 0;
      var parts = o.partials || [[1, 1], [2.0, 0.5], [2.76, 0.35], [5.4, 0.12]];
      for (var i = 0; i < parts.length; i++) {
        var r = parts[i][0], a = parts[i][1];
        T({ f: o.f * r, t0: t0, dur: o.dur / Math.sqrt(r), peak: o.peak * a, att: o.att || 0.004 });
      }
    }
    function C(o) { // creak: AM band-noise {t0?, dur, f?, rate?, peak}
      var t0 = o.t0 || 0, f = o.f || 340, rate = o.rate || 6;
      var n = Math.ceil(o.dur * SR) + 8;
      var b = ctx.createBuffer(1, n, SR), d = b.getChannelData(0);
      for (var i = 0; i < n; i++) d[i] = Math.random() * 2 - 1;
      var s = ctx.createBufferSource(); s.buffer = b;
      var bp = ctx.createBiquadFilter(); bp.type = 'bandpass';
      bp.frequency.setValueAtTime(f, t0);
      bp.frequency.linearRampToValueAtTime(f * 0.68, t0 + o.dur);
      bp.Q.value = 8;
      var g = envGain(t0, o.peak, o.dur * 0.3, o.dur);
      g.disconnect();
      var am = ctx.createGain(); am.gain.value = 0.55;
      var lfo = ctx.createOscillator();
      lfo.frequency.setValueAtTime(rate, t0);
      lfo.frequency.linearRampToValueAtTime(rate * 0.55, t0 + o.dur);
      var lg = ctx.createGain(); lg.gain.value = 0.45;
      lfo.connect(lg); lg.connect(am.gain);
      s.connect(bp); bp.connect(g); g.connect(am); am.connect(dest);
      s.start(t0); lfo.start(t0); lfo.stop(t0 + o.dur + 0.02);
    }
    function S(o) { // sample: {buf, t0?, peak?, lp?, hp?}
      var t0 = o.t0 || 0;
      var s = ctx.createBufferSource(); s.buffer = o.buf;
      var node = s;
      if (o.lp) { var f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = o.lp; node.connect(f); node = f; }
      if (o.hp) { var h = ctx.createBiquadFilter(); h.type = 'highpass'; h.frequency.value = o.hp; node.connect(h); node = h; }
      var g = ctx.createGain(); g.gain.value = (o.peak == null ? 1 : o.peak);
      node.connect(g); g.connect(dest);
      s.start(t0);
    }
    return { T: T, Nz: Nz, B: B, C: C, S: S };
  }

  // Note frequencies (key of D)
  var D2 = 73.42, A2 = 110, D3 = 146.83, F3 = 174.61, F$3 = 185, A3 = 220,
      D4 = 293.66, F$4 = 369.99, A4 = 440, D5 = 587.33, E5 = 659.25,
      F$5 = 739.99, A5 = 880, B5 = 987.77, D6 = 1174.66, E6 = 1318.51,
      F$6 = 1479.98, A6 = 1760, D7 = 2349.32;

  var GONG = [[1, 1], [1.48, 0.6], [2.31, 0.4], [3.17, 0.25], [4.5, 0.12]];
  var METAL = [[1, 1], [1.51, 0.7], [2.7, 0.5], [4.2, 0.25], [6.8, 0.12]];
  var MINOR = [[1, 1], [1.19, 0.5], [2.0, 0.4], [2.98, 0.2]];

  // cues: id -> [duration, fn(toolkit)]
  var PALETTES = {
    wood: {
      name: 'Timber & Stone',
      tag: 'Tabletop organic — wood, felt and stone, like pieces on a board. Cozy, dry, close-mic\u2019d.',
      cues: {
        click: [0.08, function (k) { k.Nz({ dur: 0.012, peak: 0.35, lp: 2600 }); k.T({ f: 1050, dur: 0.05, peak: 0.28 }); k.T({ f: 2100, dur: 0.025, peak: 0.1 }); }],
        place: [0.17, function (k) { k.Nz({ dur: 0.03, peak: 0.5, lp: 750 }); k.T({ f: 196, f1: 172, dur: 0.14, peak: 0.55 }); k.T({ f: 430, dur: 0.05, peak: 0.16 }); }],
        tower_place: [0.24, function (k) { k.Nz({ dur: 0.05, peak: 0.55, lp: 500 }); k.Nz({ dur: 0.03, peak: 0.18, hp: 1800 }); k.T({ f: 142, f1: 120, dur: 0.2, peak: 0.55 }); k.T({ f: 355, dur: 0.06, peak: 0.14 }); }],
        unit_combine: [0.55, function (k) { var seq = [[D5, 0], [F$5, 0.09], [A5, 0.18]]; for (var i = 0; i < 3; i++) { k.T({ f: seq[i][0], t0: seq[i][1], dur: 0.3, peak: 0.3 }); k.T({ f: seq[i][0] * 4, t0: seq[i][1], dur: 0.1, peak: 0.05 }); } }],
        unit_destroyed: [0.24, function (k) { k.Nz({ dur: 0.18, peak: 0.4, lp: 900, lp1: 280 }); k.T({ f: 240, f1: 90, dur: 0.17, peak: 0.35 }); }],
        tower_destroyed: [0.5, function (k) { k.Nz({ dur: 0.12, peak: 0.6, lp: 480 }); k.Nz({ t0: 0.04, dur: 0.1, peak: 0.18, hp: 1500 }); k.Nz({ t0: 0.13, dur: 0.08, peak: 0.1, hp: 2500 }); k.T({ f: 110, f1: 48, dur: 0.32, peak: 0.5 }); }],
        tree_cleared: [0.2, function (k) { k.Nz({ dur: 0.015, peak: 0.5, hp: 2000 }); k.T({ f: 330, f1: 296, dur: 0.07, peak: 0.35 }); k.Nz({ t0: 0.01, dur: 0.09, peak: 0.22, lp: 1800, hp: 700 }); }],
        capital_destroyed: [0.95, function (k) { var kn = [[230, 0], [185, 0.12], [150, 0.26]]; for (var i = 0; i < 3; i++) { k.T({ f: kn[i][0], t0: kn[i][1], dur: 0.09, peak: 0.32 }); k.Nz({ t0: kn[i][1], dur: 0.02, peak: 0.26, lp: 900 }); } k.T({ f: 98, f1: 58, t0: 0.26, dur: 0.3, peak: 0.4 }); k.B({ f: D3, t0: 0.32, dur: 0.55, peak: 0.2 }); }],
        bankruptcy: [1.35, function (k) { k.B({ f: D3, dur: 1.25, peak: 0.5, partials: [[1, 1], [2.0, 0.45], [2.4, 0.3], [4.1, 0.1]] }); }],
        game_won: [1.9, function (k) { var seq = [[D5, 0], [F$5, 0.14], [A5, 0.28], [D6, 0.44]]; for (var i = 0; i < 4; i++) k.B({ f: seq[i][0], t0: seq[i][1], dur: 0.5, peak: 0.26 }); k.B({ f: D6, t0: 0.7, dur: 1.05, peak: 0.34 }); k.B({ f: D3, t0: 0.44, dur: 0.9, peak: 0.16 }); }],
        rally: [0.48, function (k) { k.Nz({ dur: 0.4, peak: 0.42, att: 0.13, lp: 2600, lp1: 850, hp: 500 }); }],
        player_defeated: [1.75, function (k) { k.B({ f: D2, dur: 1.6, peak: 0.55, partials: GONG }); }],
        tile_submerged: [0.55, function (k) { k.T({ f: 520, f1: 300, dur: 0.08, peak: 0.25 }); k.T({ f: 430, f1: 240, t0: 0.12, dur: 0.09, peak: 0.22 }); k.T({ f: 340, f1: 180, t0: 0.26, dur: 0.1, peak: 0.2 }); k.Nz({ dur: 0.4, peak: 0.18, att: 0.05, lp: 800 }); }],
        viking_arrival: [1.75, function (k) { k.Nz({ dur: 1.2, peak: 0.25, att: 0.3, lp: 420 }); k.C({ t0: 0.5, dur: 0.7, f: 340, rate: 6.5, peak: 0.38 }); k.C({ t0: 1.15, dur: 0.5, f: 280, rate: 4, peak: 0.24 }); }],
        reject_generic: [0.13, function (k) { k.T({ f: 165, f1: 140, dur: 0.1, peak: 0.4 }); k.Nz({ dur: 0.02, peak: 0.25, lp: 600 }); }],
        reject_defended: [0.38, function (k) { k.B({ f: 520, dur: 0.32, peak: 0.4, partials: METAL }); k.Nz({ dur: 0.02, peak: 0.2, hp: 3000 }); }],
        gold_captured: [0.5, function (k) { k.T({ f: D6, dur: 0.22, peak: 0.25 }); k.T({ f: A6, t0: 0.08, dur: 0.28, peak: 0.25 }); k.T({ f: D7, t0: 0.16, dur: 0.16, peak: 0.06, type: 'triangle' }); }],
        mountain_captured: [0.34, function (k) { k.T({ f: 110, f1: 52, dur: 0.2, peak: 0.5 }); k.Nz({ dur: 0.28, peak: 0.4, lp: 420 }); }]
      }
    },
    guild: {
      name: 'Guildhall',
      tag: 'Storybook orchestral — felt mallets, harps, bells and low drums. Warm, rounded, a little grand.',
      cues: {
        click: [0.08, function (k) { k.T({ f: D6, dur: 0.06, peak: 0.22, att: 0.012 }); k.T({ f: D7, dur: 0.03, peak: 0.05, att: 0.012 }); }],
        place: [0.26, function (k) { k.T({ f: D3, dur: 0.22, peak: 0.5, att: 0.015 }); k.T({ f: D4, dur: 0.08, peak: 0.12, att: 0.012 }); k.Nz({ dur: 0.04, peak: 0.2, lp: 400 }); }],
        tower_place: [0.34, function (k) { k.T({ f: A2, f1: 100, dur: 0.3, peak: 0.55, att: 0.012 }); k.Nz({ dur: 0.06, peak: 0.3, lp: 300 }); }],
        unit_combine: [0.9, function (k) { k.T({ f: D5, dur: 0.5, peak: 0.24, att: 0.008 }); k.T({ f: A5, t0: 0.1, dur: 0.55, peak: 0.24, att: 0.008 }); k.T({ f: D6, t0: 0.2, dur: 0.6, peak: 0.28, att: 0.008 }); k.T({ f: F$6, t0: 0.32, dur: 0.3, peak: 0.07, att: 0.008 }); }],
        unit_destroyed: [0.38, function (k) { k.T({ f: 130, f1: 85, dur: 0.3, peak: 0.5, att: 0.01 }); k.Nz({ dur: 0.08, peak: 0.3, lp: 350 }); }],
        tower_destroyed: [0.5, function (k) { k.T({ f: 95, f1: 58, dur: 0.35, peak: 0.55, att: 0.008 }); k.Nz({ dur: 0.3, peak: 0.1, hp: 4000, att: 0.005 }); k.Nz({ dur: 0.1, peak: 0.4, lp: 500 }); }],
        tree_cleared: [0.13, function (k) { k.T({ f: 800, dur: 0.05, peak: 0.35 }); k.T({ f: 1200, dur: 0.03, peak: 0.15 }); k.Nz({ dur: 0.02, peak: 0.3, lp: 2500, hp: 900 }); }],
        capital_destroyed: [1.2, function (k) { var hits = [0, 0.16, 0.32]; for (var i = 0; i < 3; i++) k.T({ f: 110, f1: 74, t0: hits[i], dur: 0.2, peak: 0.38, att: 0.008 }); k.B({ f: D4, t0: 0.42, dur: 0.72, peak: 0.3, partials: MINOR }); }],
        bankruptcy: [1.05, function (k) {
          // Arcade "game over" womp, softened: triangle lead + sine sub so it
          // reads chip-like without square-wave harshness.
          var seq = [[D4, 0], [A3, 0.15], [F3, 0.3]];
          for (var i = 0; i < 3; i++) {
            k.T({ f: seq[i][0], t0: seq[i][1], dur: 0.15, peak: 0.24, type: 'triangle', att: 0.01 });
            k.T({ f: seq[i][0] / 2, t0: seq[i][1], dur: 0.15, peak: 0.14, att: 0.01 });
          }
          k.T({ f: D3, f1: 138, t0: 0.45, dur: 0.55, peak: 0.26, type: 'triangle', att: 0.01 });
          k.T({ f: D2, f1: 69, t0: 0.45, dur: 0.55, peak: 0.16, att: 0.01 });
        }],
        game_won: [1.95, function (k) { var seq = [[D5, 0], [F$5, 0.12], [A5, 0.24], [D6, 0.36], [F$6, 0.48]]; for (var i = 0; i < 5; i++) k.B({ f: seq[i][0], t0: seq[i][1], dur: 0.5, peak: 0.24 }); k.B({ f: D6, t0: 0.66, dur: 1.15, peak: 0.3 }); k.B({ f: A6, t0: 0.72, dur: 0.9, peak: 0.14 }); }],
        rally: [0.55, function (k) { k.T({ f: A3, dur: 0.42, peak: 0.25, att: 0.15, type: 'triangle' }); k.Nz({ dur: 0.42, peak: 0.2, att: 0.12, lp: 2000, hp: 600 }); }],
        player_defeated: [2.5, function (k) {
          // Longer and more in-family: soft low drum onset, D2 gong bloom, and
          // a somber D3 minor bell overlay tying it to the capital/win bells.
          k.T({ f: 55, f1: 38, dur: 0.6, peak: 0.38, att: 0.012 });
          k.B({ f: D2, dur: 2.3, peak: 0.5, partials: GONG, att: 0.006 });
          k.B({ f: D3, t0: 0.25, dur: 1.6, peak: 0.17, partials: MINOR });
        }],
        tile_submerged: [0.6, function (k) { k.T({ f: A4, dur: 0.25, peak: 0.2, att: 0.008 }); k.T({ f: F$4, t0: 0.1, dur: 0.26, peak: 0.2, att: 0.008 }); k.T({ f: D4, t0: 0.2, dur: 0.3, peak: 0.22, att: 0.008 }); k.Nz({ dur: 0.32, peak: 0.15, att: 0.04, lp: 700 }); }],
        viking_arrival: [2.6, function (k, s) {
          // Base = the ORIGINAL recorded clip (wave wash + hull creak), warmed
          // to fit Guildhall: gently darkened, with a hall drone swelling under
          // it and a soft distant drum as the creak lands.
          k.S({ buf: s.viking, peak: 0.9, lp: 3600 });
          k.T({ f: A2, t0: 0.3, dur: 1.7, peak: 0.09, att: 0.6, type: 'triangle' });
          k.T({ f: D2, f1: 55, t0: 1.05, dur: 0.55, peak: 0.2, att: 0.012 });
        }, { viking: 'audio-original/viking_arrival.wav' }],
        reject_generic: [0.14, function (k) { k.T({ f: F3, dur: 0.11, peak: 0.35, att: 0.012 }); }],
        reject_defended: [0.42, function (k) { k.B({ f: 466, dur: 0.36, peak: 0.4, partials: [[1, 1], [1.5, 0.6], [2.4, 0.4], [3.8, 0.2]] }); k.Nz({ dur: 0.015, peak: 0.18, hp: 3500 }); }],
        gold_captured: [0.52, function (k) { k.T({ f: D6, dur: 0.26, peak: 0.22, att: 0.005 }); k.T({ f: F$6, t0: 0.07, dur: 0.28, peak: 0.22, att: 0.005 }); k.T({ f: A6, t0: 0.14, dur: 0.3, peak: 0.22, att: 0.005 }); }],
        mountain_captured: [0.38, function (k) { k.T({ f: 90, f1: 55, dur: 0.3, peak: 0.55, att: 0.008 }); k.Nz({ dur: 0.15, peak: 0.3, lp: 250 }); }]
      }
    },
    slate: {
      name: 'Slate',
      tag: 'Clean game-UI — glassy plinks and tidy synth thumps. Tight envelopes, modern, unobtrusive.',
      cues: {
        click: [0.05, function (k) { k.T({ f: 2200, dur: 0.03, peak: 0.25, att: 0.002 }); k.T({ f: 4400, dur: 0.012, peak: 0.05, att: 0.002 }); }],
        place: [0.15, function (k) { k.T({ f: 170, f1: 95, dur: 0.12, peak: 0.5, att: 0.004 }); k.Nz({ dur: 0.008, peak: 0.2, lp: 1200 }); }],
        tower_place: [0.2, function (k) { k.T({ f: 130, f1: 70, dur: 0.16, peak: 0.55, att: 0.004 }); k.T({ f: 260, dur: 0.03, peak: 0.06, type: 'square' }); k.Nz({ dur: 0.012, peak: 0.22, lp: 900 }); }],
        unit_combine: [0.38, function (k) { k.T({ f: D5, dur: 0.12, peak: 0.28 }); k.T({ f: A5, t0: 0.05, dur: 0.12, peak: 0.28 }); k.T({ f: D6, t0: 0.1, dur: 0.16, peak: 0.28 }); k.T({ f: D7, t0: 0.16, dur: 0.1, peak: 0.05 }); }],
        unit_destroyed: [0.2, function (k) { k.T({ f: 420, f1: 110, dur: 0.14, peak: 0.35 }); k.Nz({ dur: 0.12, peak: 0.25, lp: 1400, lp1: 300 }); }],
        tower_destroyed: [0.3, function (k) { k.Nz({ dur: 0.2, peak: 0.5, lp: 2000, lp1: 200 }); k.T({ f: 120, f1: 50, dur: 0.22, peak: 0.45 }); }],
        tree_cleared: [0.1, function (k) { k.Nz({ dur: 0.01, peak: 0.4, hp: 3000 }); k.T({ f: 900, f1: 700, dur: 0.05, peak: 0.3 }); }],
        capital_destroyed: [0.52, function (k) { k.T({ f: D4, dur: 0.12, peak: 0.3 }); k.T({ f: A3, t0: 0.12, dur: 0.16, peak: 0.3 }); k.T({ f: 85, f1: 55, t0: 0.24, dur: 0.2, peak: 0.45 }); }],
        bankruptcy: [1.15, function (k) { k.T({ f: D3, dur: 1.0, peak: 0.3, att: 0.03 }); k.T({ f: F3, dur: 0.9, peak: 0.2, att: 0.03 }); k.T({ f: D2, dur: 0.8, peak: 0.15, att: 0.03 }); }],
        game_won: [1.65, function (k) { var seq = [[D5, 0], [F$5, 0.09], [A5, 0.18], [D6, 0.27], [F$6, 0.36]]; for (var i = 0; i < 5; i++) k.T({ f: seq[i][0], t0: seq[i][1], dur: 0.32, peak: 0.26 }); k.T({ f: D6, t0: 0.52, dur: 0.95, peak: 0.18, att: 0.2 }); k.T({ f: A6, t0: 0.52, dur: 0.85, peak: 0.1, att: 0.2 }); k.T({ f: D7, t0: 0.62, dur: 0.4, peak: 0.05, type: 'triangle' }); }],
        rally: [0.45, function (k) { k.Nz({ dur: 0.18, peak: 0.3, att: 0.1, lp: 600, lp1: 2600 }); k.Nz({ t0: 0.18, dur: 0.22, peak: 0.3, lp: 2600, lp1: 700 }); }],
        player_defeated: [1.55, function (k) { k.T({ f: 58, f1: 36, dur: 0.8, peak: 0.5, att: 0.01 }); k.B({ f: D3, t0: 0.1, dur: 1.25, peak: 0.25, partials: [[1, 1], [1.19, 0.4], [2.0, 0.25]] }); }],
        tile_submerged: [0.45, function (k) { k.T({ f: 480, f1: 240, dur: 0.1, peak: 0.25 }); k.T({ f: 360, f1: 170, t0: 0.14, dur: 0.12, peak: 0.22 }); k.Nz({ dur: 0.28, peak: 0.16, att: 0.03, lp: 600 }); }],
        viking_arrival: [1.65, function (k) { k.T({ f: 92, dur: 1.15, peak: 0.25, att: 0.3, type: 'triangle' }); k.T({ f: 138, dur: 1.0, peak: 0.12, att: 0.3, type: 'triangle' }); k.C({ t0: 0.5, dur: 0.7, f: 260, rate: 4.5, peak: 0.3 }); }],
        reject_generic: [0.09, function (k) { k.T({ f: 196, f1: 178, dur: 0.065, peak: 0.35, att: 0.003 }); }],
        reject_defended: [0.18, function (k) { k.T({ f: E5, dur: 0.12, peak: 0.3, att: 0.003 }); k.T({ f: 932.33, dur: 0.1, peak: 0.25, att: 0.003 }); k.Nz({ dur: 0.015, peak: 0.15, hp: 2500 }); }],
        gold_captured: [0.34, function (k) { k.T({ f: D6, dur: 0.12, peak: 0.28 }); k.T({ f: A6, t0: 0.06, dur: 0.16, peak: 0.28 }); k.T({ f: D7, t0: 0.12, dur: 0.1, peak: 0.05, type: 'triangle' }); }],
        mountain_captured: [0.24, function (k) { k.T({ f: 95, f1: 45, dur: 0.18, peak: 0.55, att: 0.004 }); k.Nz({ dur: 0.12, peak: 0.25, lp: 350 }); }]
      }
    }
  };

  var CUES = [
    { id: 'click', file: 'click.wav', db: -6, label: 'Button click', brief: 'UI tap on every button — felt, never noticed.' },
    { id: 'place', file: 'place.wav', db: -4, label: 'Unit placed', brief: 'The workhorse thud; a unit set on a tile.' },
    { id: 'tower_place', file: 'tower_place.wav', db: -8, label: 'Tower built', brief: 'Heavier sibling of place — stone, not wood.' },
    { id: 'unit_combine', file: 'unit_combine.wav', db: -8, label: 'Units combined', brief: 'Small level-up reward above the thud family.' },
    { id: 'unit_destroyed', file: 'unit_destroyed.wav', db: -6, label: 'Unit destroyed', brief: 'Crushing an enemy unit; soft, not gory.' },
    { id: 'tower_destroyed', file: 'tower_destroyed.wav', db: -8, label: 'Tower destroyed', brief: 'Bursting stone; loudness-matched with tower build.' },
    { id: 'tree_cleared', file: 'tree_cleared.wav', db: -18, label: 'Tree cleared', brief: 'Single chop; hot transient, sits well under place.' },
    { id: 'capital_destroyed', file: 'capital_destroyed.wav', db: -10, label: 'Capital taken', brief: 'A relocation, not a milestone — routine-event level.' },
    { id: 'bankruptcy', file: 'bankruptcy.wav', db: -10, label: 'Bankruptcy', brief: 'Somber toll at turn start; heard, never startling.' },
    { id: 'game_won', file: 'game_won.wav', db: -6, label: 'Game won', brief: 'The big one — longest cue in the set.' },
    { id: 'rally', file: 'rally.wav', db: -14, label: 'Rally', brief: 'One airy sweep per rally gesture, no impact.' },
    { id: 'player_defeated', file: 'player_defeated.wav', db: -10, label: 'Player defeated', brief: 'Deep gong — an opponent leaves the game.' },
    { id: 'tile_submerged', file: 'tile_submerged.wav', db: -8, label: 'Tile submerged', brief: 'Rising Tides glug; may fire every turn, stays quiet.' },
    { id: 'viking_arrival', file: 'viking_arrival.wav', db: -6, label: 'Viking arrival', brief: 'Threat incoming — wave wash under a hull creak.' },
    { id: 'reject_generic', file: 'reject_generic.wav', db: -17, label: 'Reject (generic)', brief: 'Misclick thunk; informative, never punishing.' },
    { id: 'reject_defended', file: 'reject_defended.wav', db: -8, label: 'Reject (defended)', brief: 'Blocked by a defender — metallic, distinct from misclick.' },
    { id: 'gold_captured', file: 'gold_captured.wav', db: -8, label: 'Gold captured', brief: 'Terrain chime; layers under the occupant cue.' },
    { id: 'mountain_captured', file: 'mountain_captured.wav', db: -8, label: 'Mountain captured', brief: 'Terrain thud; low end reads without gain.' }
  ];

  function normalize(buf) {
    var d = buf.getChannelData(0), peak = 0;
    for (var i = 0; i < d.length; i++) { var a = Math.abs(d[i]); if (a > peak) peak = a; }
    if (peak > 0) { var s = 0.707 / peak; for (var j = 0; j < d.length; j++) d[j] *= s; }
    return buf;
  }

  var _samples = {};
  function loadSample(url) {
    if (!_samples[url]) {
      _samples[url] = fetch(url)
        .then(function (r) { return r.arrayBuffer(); })
        .then(function (ab) { return new OfflineAudioContext(1, 8, SR).decodeAudioData(ab); });
    }
    return _samples[url];
  }

  function renderCue(palId, cueId) {
    var spec = PALETTES[palId].cues[cueId];
    var deps = spec[2] || {};
    var names = Object.keys(deps);
    return Promise.all(names.map(function (n) { return loadSample(deps[n]); })).then(function (bufs) {
      var samples = {};
      for (var i = 0; i < names.length; i++) samples[names[i]] = bufs[i];
      var ctx = new OfflineAudioContext(1, Math.ceil((spec[0] + 0.06) * SR), SR);
      spec[1](tk(ctx), samples);
      return ctx.startRendering().then(normalize);
    });
  }

  function bufToWav(buf) {
    var d = buf.getChannelData(0), n = d.length;
    var out = new ArrayBuffer(44 + n * 2), v = new DataView(out);
    function str(o, s) { for (var i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)); }
    str(0, 'RIFF'); v.setUint32(4, 36 + n * 2, true); str(8, 'WAVE');
    str(12, 'fmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true);
    v.setUint16(22, 1, true); v.setUint32(24, SR, true); v.setUint32(28, SR * 2, true);
    v.setUint16(32, 2, true); v.setUint16(34, 16, true);
    str(36, 'data'); v.setUint32(40, n * 2, true);
    for (var i = 0; i < n; i++) {
      var s = Math.max(-1, Math.min(1, d[i]));
      v.setInt16(44 + i * 2, s * 32767, true);
    }
    return new Uint8Array(out);
  }

  var crcTable = (function () {
    var t = new Uint32Array(256);
    for (var i = 0; i < 256; i++) {
      var c = i;
      for (var k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
      t[i] = c >>> 0;
    }
    return t;
  })();
  function crc32(u8) {
    var c = 0xFFFFFFFF;
    for (var i = 0; i < u8.length; i++) c = crcTable[(c ^ u8[i]) & 0xFF] ^ (c >>> 8);
    return (c ^ 0xFFFFFFFF) >>> 0;
  }

  function makeZip(entries) { // [{name, data:Uint8Array}] -> Uint8Array (stored)
    var parts = [], central = [], offset = 0;
    for (var e = 0; e < entries.length; e++) {
      var name = entries[e].name, data = entries[e].data;
      var nameB = new TextEncoder().encode(name);
      var crc = crc32(data);
      var lh = new ArrayBuffer(30), v = new DataView(lh);
      v.setUint32(0, 0x04034b50, true); v.setUint16(4, 20, true); v.setUint16(6, 0, true);
      v.setUint16(8, 0, true); v.setUint16(10, 0, true); v.setUint16(12, 0, true);
      v.setUint32(14, crc, true); v.setUint32(18, data.length, true); v.setUint32(22, data.length, true);
      v.setUint16(26, nameB.length, true); v.setUint16(28, 0, true);
      parts.push(new Uint8Array(lh), nameB, data);
      var ch = new ArrayBuffer(46), cv = new DataView(ch);
      cv.setUint32(0, 0x02014b50, true); cv.setUint16(4, 20, true); cv.setUint16(6, 20, true);
      cv.setUint16(8, 0, true); cv.setUint16(10, 0, true); cv.setUint16(12, 0, true); cv.setUint16(14, 0, true);
      cv.setUint32(16, crc, true); cv.setUint32(20, data.length, true); cv.setUint32(24, data.length, true);
      cv.setUint16(28, nameB.length, true);
      cv.setUint32(42, offset, true);
      central.push(new Uint8Array(ch), nameB);
      offset += 30 + nameB.length + data.length;
    }
    var cdSize = 0;
    for (var c = 0; c < central.length; c++) cdSize += central[c].length;
    var eocd = new ArrayBuffer(22), ev = new DataView(eocd);
    ev.setUint32(0, 0x06054b50, true); ev.setUint16(8, entries.length, true); ev.setUint16(10, entries.length, true);
    ev.setUint32(12, cdSize, true); ev.setUint32(16, offset, true);
    var total = offset + cdSize + 22, out = new Uint8Array(total), p = 0;
    var all = parts.concat(central, [new Uint8Array(eocd)]);
    for (var a = 0; a < all.length; a++) { out.set(all[a], p); p += all[a].length; }
    return out;
  }

  window.SfxEngine = { SR: SR, CUES: CUES, PALETTES: PALETTES, renderCue: renderCue, bufToWav: bufToWav, makeZip: makeZip };
})();
