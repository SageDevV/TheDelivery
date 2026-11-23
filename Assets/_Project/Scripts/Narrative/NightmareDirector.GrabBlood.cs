using UnityEngine;

namespace TheDelivery.Narrative
{
    // SANGUE NO ENFORCAMENTO (v13 → v14 "deve sair mais sangue", 2026-11-23). As garras dele fecham por trás da
    // nuca dela; dali sai sangue em três camadas:
    //  1. GOTAS — respingos no aperto, na sacudida e na soltura, e um gotejar que espaça conforme ela enfraquece;
    //  2. FILETE — um fio contínuo de gotas finas escorrendo da nuca logo depois que o aperto fecha;
    //  3. MANCHA — manchas que se acumulam na nuca e no alto das costas do moletom (presas ao corpo dela,
    //     vão junto no voo).
    // Cada gota é emitida à mão (ParticleSystem.Emit) com posição e velocidade escolhidas aqui.
    //
    // POR QUE COM IMPULSO PARA FORA: o pescoço está em cima do corpo dela. Uma gota solta no pescoço e
    // caindo reta atravessa o tronco e reaparece embaixo dos pés. Saindo da nuca (o lado das garras) para
    // trás e para os lados, ela passa por fora das costas — o lado que a câmera de perfil vê contra o céu.
    public sealed partial class NightmareDirector
    {
        private ParticleSystem grabBloodFx, grabBloodStainFx;
        private Transform grabBloodAnchor; // segue o pescoço dela (posição) e o corpo (rotação), escala 1
        private bool grabBloodDripping;
        private float grabBloodNext, grabBloodTrickleNext, grabBloodStainNext, grabBloodStart;
        private int grabBloodStains;
        private Vector3 grabBloodNeckPrev, grabBloodNeckVel;
        private bool grabBloodPrimed;

        private const int BloodStainMax = 16;
        private const float BloodTrickleDuration = 2.4f; // o filete dura do aperto até perto do fim da sacudida

        /// <summary>O aperto fechou: o primeiro respingo, o filete e o começo do gotejar e da mancha.</summary>
        private void BeginGrabBlood()
        {
            if (!grabBlood || grabTakeActor == null || grabTakeActorNeck == null) return;
            if (grabBloodFx == null) grabBloodFx = CreateBloodFx(false);
            if (grabBloodStainFx == null) grabBloodStainFx = CreateBloodFx(true);
            grabBloodDripping = true;
            grabBloodStart = Time.time;
            grabBloodNext = Time.time + 0.15f;
            grabBloodTrickleNext = Time.time;
            grabBloodStainNext = Time.time;
            grabBloodStains = 0;
            BloodBurst(12, 0.45f, 1.1f);
            for (int i = 0; i < 4; i++) EmitBloodStain(); // o aperto já marca a nuca
        }

        /// <summary>Um respingo de <paramref name="count"/> gotas (o aperto, a sacudida, o giro). Um terço sai graúdo.</summary>
        private void BloodBurst(int count, float speedMin, float speedMax)
        {
            if (grabBloodFx == null || grabTakeActorNeck == null) return;
            for (int i = 0; i < count; i++)
            {
                float size = Random.value < 0.33f ? Random.Range(0.042f, 0.058f) : Random.Range(0.024f, 0.04f);
                EmitBloodDrop(Random.Range(speedMin, speedMax), size);
            }
        }

        /// <summary>A mão abriu: gotejar e filete param (o que está no ar continua caindo) e sai um respingo no embalo.</summary>
        private void StopGrabBlood()
        {
            if (grabBloodFx == null) return;
            if (grabBloodDripping) BloodBurst(10, 0.3f, 0.9f);
            grabBloodDripping = false;
        }

        /// <summary>Um quadro (LateUpdate, depois do aperto): segue o pescoço, goteja, escorre e mancha no ritmo da fase.</summary>
        private void UpdateGrabBlood()
        {
            if (grabTakeActorNeck == null || grabTakeActor == null) { grabBloodPrimed = false; return; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            Vector3 neck = grabTakeActorNeck.position;
            if (grabBloodPrimed) grabBloodNeckVel = Vector3.Lerp(grabBloodNeckVel, (neck - grabBloodNeckPrev) / dt, 0.5f);
            grabBloodNeckPrev = neck; grabBloodPrimed = true;
            if (grabBloodAnchor != null) grabBloodAnchor.SetPositionAndRotation(neck, grabTakeActor.transform.rotation);
            if (!grabBloodDripping || grabBloodFx == null) return;

            float t = ClipTimeSeconds(out float len);
            float tl = grabHeldLaughNormalizedTime * len, te = grabLaughEndNormalizedTime * len;
            float since = Time.time - grabBloodStart;

            // GOTEJAR: vivo logo depois do aperto, espaçando conforme ela enfraquece
            if (Time.time >= grabBloodNext)
            {
                int n = Random.value < 0.35f ? 2 : 1;
                for (int i = 0; i < n; i++) EmitBloodDrop(Random.Range(0.1f, 0.45f), Random.Range(0.022f, 0.038f));
                grabBloodNext = Time.time + (t < tl ? Random.Range(0.06f, 0.14f) : t < te ? Random.Range(0.14f, 0.3f) : Random.Range(0.28f, 0.5f));
            }
            // FILETE: um fio de gotas finas, quase sem impulso, que afina com o tempo
            if (since < BloodTrickleDuration && Time.time >= grabBloodTrickleNext)
            {
                EmitBloodDrop(Random.Range(0.04f, 0.14f), Random.Range(0.012f, 0.02f), trickle: true);
                grabBloodTrickleNext = Time.time + Mathf.Lerp(0.025f, 0.07f, since / BloodTrickleDuration);
            }
            // MANCHA: cresce no moletom enquanto o sangue escorre
            if (grabBloodStains < BloodStainMax && Time.time >= grabBloodStainNext)
            {
                EmitBloodStain();
                grabBloodStainNext = Time.time + Random.Range(0.25f, 0.5f);
            }
        }

        private void EmitBloodDrop(float speed, float size, bool trickle = false)
        {
            Transform actor = grabTakeActor != null ? grabTakeActor.transform : null;
            if (actor == null) return;
            Vector3 up = actor.up;
            Vector3 back = Vector3.ProjectOnPlane(-actor.forward, up).normalized; // a nuca: o lado das garras
            Vector3 side = Vector3.Cross(up, back).normalized;
            float ang = (trickle ? Random.Range(-12f, 12f) : Random.Range(-38f, 38f)) * Mathf.Deg2Rad;
            Vector3 outward = (back * Mathf.Cos(ang) + side * Mathf.Sin(ang)).normalized;
            Vector3 pos = grabTakeActorNeck.position + outward * (trickle ? 0.09f : 0.07f) + up * Random.Range(-0.06f, 0.0f);
            Vector3 vel = outward * speed + up * (trickle ? 0f : Random.Range(0.0f, 0.2f)) + grabBloodNeckVel * 0.75f;
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = Random.Range(1.8f, 2.6f),
                // VERMELHO-SANGUE médio: vinho escuro virava risco preto (lia como chuva) e vermelho vivo
                // sumia no céu vermelho. Este destaca do céu sem virar preto.
                startColor = Color.Lerp(new Color(0.4f, 0.012f, 0.025f, 1f), new Color(0.66f, 0.03f, 0.04f, 1f), Random.value),
                applyShapeToPosition = false
            };
            grabBloodFx.Emit(p, 1);
        }

        /// <summary>Uma mancha no espaço do corpo dela: nuca e alto das costas, escorrendo para baixo com o tempo.</summary>
        private void EmitBloodStain()
        {
            if (grabBloodStainFx == null) return;
            float k = grabBloodStains / (float)BloodStainMax; // as últimas descem mais pelas costas
            var p = new ParticleSystem.EmitParams
            {
                // âncora: +Z = de frente para a criatura; a nuca é -Z. Um pouco para fora da superfície do moletom.
                position = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.03f, 0.02f) - k * 0.16f, -Random.Range(0.085f, 0.105f)),
                velocity = Vector3.zero,
                startSize = Random.Range(0.035f, 0.07f) * Mathf.Lerp(1f, 0.75f, k),
                startLifetime = 60f,
                startColor = Color.Lerp(new Color(0.3f, 0.008f, 0.02f, 0.95f), new Color(0.48f, 0.02f, 0.03f, 0.95f), Random.value),
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false
            };
            grabBloodStainFx.Emit(p, 1);
            grabBloodStains++;
        }

        private float ClipTimeSeconds(out float len)
        {
            len = 1f;
            if (grabAnimator == null) return 0f;
            AnimatorStateInfo st = grabAnimator.GetCurrentAnimatorStateInfo(0);
            len = Mathf.Max(0.01f, st.length);
            return st.normalizedTime * len;
        }

        /// <param name="stain">true = manchas presas ao corpo dela (espaço da âncora, sem gravidade, sem esticar).</param>
        private ParticleSystem CreateBloodFx(bool stain)
        {
            var go = new GameObject(stain ? "ClearBloodStain" : "ClearBloodDrops");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 30f;
            main.maxParticles = stain ? 32 : 600;
            main.startSpeed = 0f;
            if (stain)
            {
                if (grabBloodAnchor == null) grabBloodAnchor = new GameObject("ClearBloodAnchor").transform;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = grabBloodAnchor;
                main.gravityModifier = 0f;
            }
            else
            {
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 1f;
                // some no fim (gotas que já caíram longe no abismo)
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
            }
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (stain)
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Stretch; // a gota estica com a velocidade da queda
                r.velocityScale = 0.018f; // pouco: uma gota alongada, não um risco de chuva
                r.lengthScale = 1.15f;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = grabBloodMaterial != null ? grabBloodMaterial : FallbackBloodMaterial();
            ps.Play();
            return ps;
        }

        private static Material FallbackBloodMaterial()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(s) { name = "BloodDrop (runtime)" };
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        /// <summary>Desmonta junto com o take (as gotas no ar ainda caem por uns segundos).</summary>
        private void CleanupGrabBlood()
        {
            grabBloodDripping = false;
            grabBloodPrimed = false;
            if (grabBloodFx != null) Destroy(grabBloodFx.gameObject, 3f);
            if (grabBloodStainFx != null) Destroy(grabBloodStainFx.gameObject);
            if (grabBloodAnchor != null) Destroy(grabBloodAnchor.gameObject);
            grabBloodFx = null; grabBloodStainFx = null; grabBloodAnchor = null;
        }
    }
}
