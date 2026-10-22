using System;
using System.Collections.Generic;

// Kodla üretilen synthwave müzik (ses dosyası yok). Ana menüde çalar (MenuMusic, OnAudioFilterRead'den Render'ı çağırır).
// Unity'ye bağımlı değil (sadece System): Araclar/Muzik/run.ps1 aynı dosyayı Unity'siz .wav'a yazıp ölçer.
//
// 112 BPM, La minör, akor dizisi Am – F – C – G (her biri 2 ölçü, 8 ölçülük döngü).
// Enstrümanlar: dört vuruşlu davul (kick, büyük yankılı snare, 16'lık hi-hat, açık hat), sekizlik oktav basları
// (kick'te nefes alır: sidechain), dedektörlü saw pad, 16'lık arpej (noktalı sekizlik ping-pong yankı), saw lead melodi.
// Yapı: intro (pad + filtreli arpej) → hazırlık (+kick, hat, bas) → ana bölüm (+snare) → ara (pad + lead) → ana 2 (+lead) → çıkış.
public class SynthwaveMusic
{
    public const float Bpm = 112f;
    public const int SongBars = 30;
    readonly float sr;
    readonly float spb;                          // vuruş başına saniye
    readonly Random rng = new Random(5);
    long n;                                      // çalınan örnek sayısı
    int scheduledBar = -1;

    // ---------------- Akorlar ----------------
    static readonly int[] Roots = { 33, 29, 36, 31 };                         // A1, F1, C2, G1 (bas)
    static readonly int[][] Pads = { new[] { 57, 60, 64 }, new[] { 57, 60, 65 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 } };
    // Lead melodi (8 ölçü = 32 vuruş): başlangıç vuruşu, süre (vuruş), nota
    static readonly float[,] Melody =
    {
        { 0, 1.5f, 76 }, { 1.5f, 0.5f, 74 }, { 2, 1, 72 }, { 3, 1, 74 }, { 4, 3, 76 }, { 7, 1, 79 },
        { 8, 1.5f, 77 }, { 9.5f, 0.5f, 76 }, { 10, 1, 74 }, { 11, 1, 72 }, { 12, 3, 74 }, { 15, 1, 72 },
        { 16, 1.5f, 72 }, { 17.5f, 0.5f, 74 }, { 18, 1, 76 }, { 19, 1, 79 }, { 20, 3, 76 }, { 23, 1, 74 },
        { 24, 1.5f, 74 }, { 25.5f, 0.5f, 72 }, { 26, 1, 71 }, { 27, 1, 74 }, { 28, 4, 69 },
    };

    enum Inst { Kick, Snare, Hat, OpenHat, Bass, Pad, Arp, Lead }

    class Voice
    {
        public Inst inst;
        public long start, gateEnd;              // örnek
        public float freq, pan, gain, cut, ph1, ph2, ph3, s1, s2, nlp;
        public bool done;
    }
    readonly List<Voice> voices = new List<Voice>();

    // ---------------- Efektler ----------------
    class Comb
    {
        readonly float[] buf; int i; float store; readonly float fb, damp;
        public Comb(int len, float fb, float damp) { buf = new float[len]; this.fb = fb; this.damp = damp; }
        public float Run(float x) { float y = buf[i]; store = y * (1f - damp) + store * damp; buf[i] = x + store * fb; if (++i >= buf.Length) i = 0; return y; }
    }
    class AllPass
    {
        readonly float[] buf; int i;
        public AllPass(int len) { buf = new float[len]; }
        public float Run(float x) { float b = buf[i]; float y = -x + b; buf[i] = x + b * 0.5f; if (++i >= buf.Length) i = 0; return y; }
    }
    readonly Comb[] combL, combR;
    readonly AllPass[] apL, apR;
    readonly float[] dlyL, dlyR;
    int dlyW, dlyLen;
    float dlyLp, hpL, hpLin, hpR, hpRin;
    float duck = 1f;                             // sidechain (kick'te pad ve bas kısılır)
    long lastKick = -100000;

    public SynthwaveMusic(float sampleRate)
    {
        sr = sampleRate;
        spb = 60f / Bpm;
        float k = sr / 44100f;
        int[] cl = { 1557, 1617, 1491, 1422 };
        combL = new Comb[4]; combR = new Comb[4];
        for (int j = 0; j < 4; j++) { combL[j] = new Comb((int)(cl[j] * k), 0.86f, 0.35f); combR[j] = new Comb((int)((cl[j] + 23) * k), 0.86f, 0.35f); }
        apL = new[] { new AllPass((int)(556 * k)), new AllPass((int)(441 * k)) };
        apR = new[] { new AllPass((int)(579 * k)), new AllPass((int)(464 * k)) };
        dlyLen = (int)(spb * 0.75f * sr);                                        // noktalı sekizlik
        dlyL = new float[dlyLen]; dlyR = new float[dlyLen];
    }

    static float Mtof(float m) => 440f * (float)Math.Pow(2.0, (m - 69f) / 12f);
    float Rand() => (float)rng.NextDouble() * 2f - 1f;
    long S(float beats) => (long)(beats * spb * sr);

    void Add(Inst inst, float beatStart, float beatLen, float midi, float gain, float pan = 0f, float cut = 0f)
    {
        voices.Add(new Voice { inst = inst, start = S(beatStart), gateEnd = S(beatStart + beatLen), freq = midi > 0 ? Mtof(midi) : 0f,
                               gain = gain, pan = pan, cut = cut, ph1 = (float)rng.NextDouble(), ph2 = (float)rng.NextDouble(), ph3 = (float)rng.NextDouble() });
    }

    // Bir ölçünün notalarını programla (müzik çalarken bir ölçü önden). Parça bitince baştan döner
    // (çıkış bölümü sakin olduğu için intro'ya yumuşak bağlanır).
    void ScheduleBar(int absBar)
    {
        float b0 = absBar * 4f;
        int bar = absBar % SongBars;
        int ch = (bar / 2) % 4;
        bool intro = bar < 4, build = bar >= 4 && bar < 8, main = bar >= 8 && bar < 16, brk = bar >= 16 && bar < 20,
             main2 = bar >= 20 && bar < 28, outro = bar >= 28;
        bool drums = build || main || main2;

        // Pad: her akor 2 ölçü (akorun ilk ölçüsünde başlar)
        if (bar % 2 == 0)
        {
            float pcut = intro ? 500f + 350f * bar : 1900f;
            foreach (int m in Pads[ch])
                for (int d = -1; d <= 1; d++)
                    Add(Inst.Pad, b0, 8f, m + d * 0.09f, outro ? 0.045f : 0.06f, d * 0.6f, pcut);
        }

        // Davul
        if (drums)
            for (int bt = 0; bt < 4; bt++)
            {
                Add(Inst.Kick, b0 + bt, 0.5f, 0, 0.75f);
                if ((main || main2) && (bt == 1 || bt == 3)) Add(Inst.Snare, b0 + bt, 0.5f, 0, 0.55f);
                for (int s = 0; s < 4; s++)
                {
                    if (s == 2 && (main || main2)) Add(Inst.OpenHat, b0 + bt + 0.5f, 0.25f, 0, 0.10f, 0.25f);
                    else Add(Inst.Hat, b0 + bt + s * 0.25f, 0.1f, 0, s == 2 ? 0.10f : 0.055f, -0.2f);
                }
            }
        if (brk)                                                                // arada sadece hafif hat
            for (int s = 0; s < 8; s++) Add(Inst.Hat, b0 + s * 0.5f, 0.1f, 0, 0.05f, -0.2f);

        // Bas: sekizlik oktav (kök, oktav yukarı)
        if (build || main || main2)
            for (int s = 0; s < 8; s++)
                Add(Inst.Bass, b0 + s * 0.5f, 0.42f, Roots[ch] + (s % 2 == 1 ? 12 : 0), 0.32f);

        // Arpej: 16'lık, akor notaları oktav yukarı
        if (!brk)
        {
            int[] p = Pads[ch];
            int[] seq = { p[0] + 12, p[1] + 12, p[2] + 12, p[0] + 24, p[2] + 12, p[1] + 12 };
            float acut = intro ? 700f + 600f * bar : 3200f;
            float ag = intro ? 0.10f : outro ? 0.08f : 0.09f;
            for (int s = 0; s < 16; s++)
                Add(Inst.Arp, b0 + s * 0.25f, 0.22f, seq[(bar * 16 + s) % seq.Length], ag, s % 2 == 0 ? -0.35f : 0.35f, acut);
        }

        // Lead: arada ilk yarı, ana 2'de bütün melodi
        if (brk || main2)
        {
            int rel = brk ? bar - 16 : (bar - 20) % 8;
            float from = rel * 4f, to = from + 4f;
            for (int i = 0; i < Melody.GetLength(0); i++)
            {
                float st = Melody[i, 0];
                if (st < from || st >= to) continue;
                Add(Inst.Lead, b0 + st - from, Melody[i, 1] * 0.95f, Melody[i, 2], brk ? 0.14f : 0.12f);
            }
        }
    }

    // ---------------- Ses ----------------
    static float Blep(float t, float dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1f; }
        if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
        return 0f;
    }
    float Saw(ref float ph, float f) { float dt = f / sr; ph += dt; if (ph >= 1f) ph -= 1f; return 2f * ph - 1f - Blep(ph, dt); }
    float Sq(ref float ph, float f)
    {
        float dt = f / sr; ph += dt; if (ph >= 1f) ph -= 1f;
        float p2 = ph + 0.5f; if (p2 >= 1f) p2 -= 1f;
        return (ph < 0.5f ? 1f : -1f) + Blep(ph, dt) - Blep(p2, dt);
    }
    // Durum değişkenli alçak geçiren (TPT); g = tan yaklaşımı
    float Lp(Voice v, float x, float fc, float q)
    {
        float w = (float)Math.PI * Math.Min(fc, sr * 0.45f) / sr;
        float g = w * (1f + w * w / 3f), k = 1f / q;
        float a1 = 1f / (1f + g * (g + k)), a2 = g * a1, a3 = g * a2;
        float v3 = x - v.s2, v1 = a1 * v.s1 + a2 * v3, v2 = v.s2 + a2 * v.s1 + a3 * v3;
        v.s1 = 2f * v1 - v.s1; v.s2 = 2f * v2 - v.s2;
        return v2;
    }
    static float Env(float t, float gate, float a, float d, float s, float r)
    {
        float e;
        if (t < a) e = t / a;
        else e = s + (1f - s) * (float)Math.Exp(-(t - a) / d);
        if (t > gate) e *= (float)Math.Exp(-(t - gate) / r);
        return e;
    }

    // Bir sesin bir örneği; dry (sol, sağ), yankı (reverb) ve gecikme (delay) gönderimi
    void VoiceSample(Voice v, float t, float gate, ref float l, ref float r, ref float rev, ref float dly)
    {
        float y = 0f, revS = 0f, dlyS = 0f;
        switch (v.inst)
        {
            case Inst.Kick:
            {
                if (t > 0.45f) { v.done = true; return; }
                float f = 45f + 115f * (float)Math.Exp(-t * 28f);
                v.ph1 += f / sr; if (v.ph1 >= 1f) v.ph1 -= 1f;
                y = (float)Math.Sin(2 * Math.PI * v.ph1) * (float)Math.Exp(-t * 6.5f) + Rand() * (float)Math.Exp(-t * 400f) * 0.25f;
                break;
            }
            case Inst.Snare:
            {
                if (t > 0.5f) { v.done = true; return; }
                float nz = Rand();
                v.nlp += 0.35f * (nz - v.nlp);
                float body = (nz - v.nlp) * (float)Math.Exp(-t * 13f);
                v.ph1 += 190f / sr; if (v.ph1 >= 1f) v.ph1 -= 1f;
                y = body + 0.5f * (float)Math.Sin(2 * Math.PI * v.ph1) * (float)Math.Exp(-t * 22f);
                revS = 0.9f;
                break;
            }
            case Inst.Hat:
            case Inst.OpenHat:
            {
                float dec = v.inst == Inst.Hat ? 70f : 9f;
                if (t > (v.inst == Inst.Hat ? 0.1f : 0.4f)) { v.done = true; return; }
                float nz = Rand();
                v.nlp += 0.6f * (nz - v.nlp);
                y = (nz - v.nlp) * (float)Math.Exp(-t * dec);                 // yüksek geçiren gürültü
                revS = 0.1f;
                break;
            }
            case Inst.Bass:
            {
                if (t > gate + 0.08f) { v.done = true; return; }
                float x = Saw(ref v.ph1, v.freq) * 0.7f + Sq(ref v.ph2, v.freq * 0.5f) * 0.3f;
                y = Lp(v, x, 260f + 1100f * (float)Math.Exp(-t * 14f), 1.3f) * Env(t, gate, 0.003f, 0.15f, 0.75f, 0.03f) * duck;
                break;
            }
            case Inst.Pad:
            {
                if (t > gate + 1.2f) { v.done = true; return; }
                float x = Saw(ref v.ph1, v.freq);
                y = Lp(v, x, v.cut, 0.8f) * Env(t, gate, 0.7f, 1.5f, 0.85f, 0.6f) * (0.55f + 0.45f * duck);
                revS = 0.5f;
                break;
            }
            case Inst.Arp:
            {
                if (t > gate + 0.25f) { v.done = true; return; }
                float x = Sq(ref v.ph1, v.freq) * 0.6f + Saw(ref v.ph2, v.freq * 1.003f) * 0.4f;
                y = Lp(v, x, v.cut + 2200f * (float)Math.Exp(-t * 28f), 1.6f) * (float)Math.Exp(-t * 9f) * Env(t, gate, 0.002f, 1f, 1f, 0.05f);
                dlyS = 0.45f; revS = 0.2f;
                break;
            }
            case Inst.Lead:
            {
                if (t > gate + 0.35f) { v.done = true; return; }
                float vib = t > 0.25f ? 1f + 0.012f * (float)Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1f, (t - 0.25f) * 3f) : 1f;
                float f = v.freq * vib;
                float x = Saw(ref v.ph1, f * 0.996f) * 0.45f + Saw(ref v.ph2, f * 1.004f) * 0.45f + Sq(ref v.ph3, f * 0.5f) * 0.25f;
                y = Lp(v, x, 2600f + 1500f * (float)Math.Exp(-t * 6f), 1.1f) * Env(t, gate, 0.012f, 0.5f, 0.8f, 0.12f);
                dlyS = 0.35f; revS = 0.35f;
                break;
            }
        }
        y *= v.gain;
        float pl = (float)Math.Sqrt(0.5f * (1f - v.pan)), pr = (float)Math.Sqrt(0.5f * (1f + v.pan));
        l += y * pl; r += y * pr;
        rev += y * revS; dly += y * dlyS;
    }

    // Akan ses üretimi: data = iç içe kanal örnekleri (oyunda OnAudioFilterRead'den çağrılır)
    public void Render(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i += channels)
        {
            int bar = (int)(n / (spb * 4f * sr));
            while (scheduledBar < bar + 1) ScheduleBar(++scheduledBar);   // bir ölçü önden programla

            float l = 0f, r = 0f, rev = 0f, dly = 0f;
            for (int k = 0; k < voices.Count; k++)
            {
                var v = voices[k];
                if (n < v.start) continue;
                if (v.inst == Inst.Kick && n == v.start) lastKick = n;
                float t = (n - v.start) / sr, gate = (v.gateEnd - v.start) / sr;
                VoiceSample(v, t, gate, ref l, ref r, ref rev, ref dly);
            }
            voices.RemoveAll(v => v.done);

            // Sidechain: kick'ten sonra pad ve bas kısılıp geri gelir (synthwave "pompası")
            float since = (n - lastKick) / sr;
            duck = 1f - 0.65f * (float)Math.Exp(-since * 9f);

            // Ping-pong gecikme (geri besleme yolunda hafif alçak geçiren)
            float oL = dlyL[dlyW], oR = dlyR[dlyW];
            dlyLp += 0.4f * (oR - dlyLp);
            dlyL[dlyW] = dly + dlyLp * 0.42f;
            dlyR[dlyW] = oL * 0.42f;
            if (++dlyW >= dlyLen) dlyW = 0;
            l += oL * 0.6f; r += oR * 0.6f;
            rev += (oL + oR) * 0.15f;

            // Yankı (Freeverb benzeri: 4 tarak + 2 tüm geçiren, kanal başına)
            float rl = 0f, rr = 0f, rin = rev * 0.04f;
            for (int c = 0; c < 4; c++) { rl += combL[c].Run(rin); rr += combR[c].Run(rin); }
            for (int a = 0; a < 2; a++) { rl = apL[a].Run(rl); rr = apR[a].Run(rr); }
            l += rl; r += rr;

            // Ana çıkış: doğru akım atma + yumuşak doyurma
            hpL = 0.999f * (hpL + l - hpLin); hpLin = l;
            hpR = 0.999f * (hpR + r - hpRin); hpRin = r;
            l = (float)Math.Tanh(hpL * 1.1f);
            r = (float)Math.Tanh(hpR * 1.1f);

            if (channels >= 2) { data[i] = l; data[i + 1] = r; for (int c = 2; c < channels; c++) data[i + c] = (l + r) * 0.5f; }
            else data[i] = (l + r) * 0.5f;
            n++;
        }
    }

    public float SongSeconds => SongBars * 4f * spb;
}
