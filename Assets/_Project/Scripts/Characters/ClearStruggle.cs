using UnityEngine;

namespace TheDelivery.Characters
{
    /// <summary>
    /// A CLEAR SE DEBATENDO NA MÃO DA CRIATURA (agarrão do pesadelo, v10 — 2026-11-23). O clipe "Held"
    /// da Meshy mexe 10–20° em seis segundos: no take ela parecia um manequim pendurado. Este
    /// componente posa o corpo dela POR CIMA do Animator, quadro a quadro, com o que a cena pede:
    /// <list type="bullet">
    /// <item><b>Mãos no pulso dele</b> — IK de dois ossos nos alvos que o diretor passa (o pulso da
    /// mão que a segura; no enforcamento, a outra mão dele também). Elas puxam, escorregam e
    /// reagarram mais acima, como quem tenta tirar a mão do pescoço.</item>
    /// <item><b>Pernas chutando</b> — ciclo de chute com frequência e força variando, falhando quando
    /// ela cansa; pés apontados (corpo suspenso).</item>
    /// <item><b>Tronco e cabeça</b> — encolhe e arqueia com os chutes, torce, o queixo sobe (o aperto
    /// empurra a cabeça para trás) e a cabeça treme.</item>
    /// <item><b>Inércia</b> — pernas e braços soltos ATRASAM o giro do corpo (mola): na sacudida e no
    /// voo eles chicoteiam em vez de girar duros junto com o tronco.</item>
    /// <item><b>Voo</b> — depois da soltura (<see cref="BeginFlight"/>): braços em moinho e pernas
    /// pedalando no ar.</item>
    /// </list>
    ///
    /// QUEM MANDA NA INTENSIDADE É O DIRETOR (<see cref="Intensity"/>, <see cref="Grip"/>,
    /// <see cref="Passive"/>), lendo a fase do clipe da criatura: desespero no enforcamento, corpo
    /// mole na sacudida, cada vez mais fraca na gargalhada. Os valores são suavizados aqui.
    ///
    /// ORDEM NO QUADRO (pendurada): o diretor gira o corpo (pêndulo) → <see cref="PoseBody"/> →
    /// prende o pescoço na palma → <see cref="PoseArms"/>. O corpo vem antes da trava do pescoço
    /// porque dobrar a coluna desloca o pescoço; os braços vêm depois porque precisam da posição
    /// final dos ombros. No voo o próprio componente posa tudo no LateUpdate.
    ///
    /// Tudo é escrito em direções no MUNDO (FromToRotation a partir da pose do clipe), então não
    /// depende dos eixos locais dos ossos do rig da Meshy.
    /// </summary>
    public sealed class ClearStruggle : MonoBehaviour
    {
        // --- o que o diretor ajusta a cada quadro (alvos; suavizados aqui) ---
        /// <summary>0 = inerte, 1 = desespero (força e ritmo dos chutes, puxões das mãos).</summary>
        public float Intensity;
        /// <summary>0 = mãos soltas (penduradas), 1 = agarradas no pulso dele.</summary>
        public float Grip;
        /// <summary>0..1: quanto pernas e braços soltos atrasam o giro do corpo (sacudida, arremesso).</summary>
        public float Passive = 0.3f;

        public bool IsBound { get; private set; }
        public bool InFlight => flight;

        // ossos
        private Transform hips, spine02, spine01, spine, neck, head, headEnd;
        private readonly Transform[] clav = new Transform[2], upper = new Transform[2], fore = new Transform[2], hand = new Transform[2];
        private readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2], toe = new Transform[2];
        private readonly float[] armA = new float[2], armB = new float[2];
        private readonly Vector3[] palmLocal = new Vector3[2]; private bool palmKnown;
        private readonly float[] twistPrev = new float[2]; private readonly bool[] twistPrimed = new bool[2];
        private Transform[] posed; private Quaternion[] restLocal;

        private void ResetToRest()
        {
            for (int i = 0; i < posed.Length; i++) posed[i].localRotation = restLocal[i];
        }

        // estado suavizado
        private float sI, sG, sP = 0.3f, jolt;
        private float clock;
        private readonly float[] legPhase = { 0f, Mathf.PI };
        private readonly float[] seed = { 3.1f, 7.7f };
        private float bodyPhase;
        // inércia (mola por membro: vetor de rotação em radianos)
        private readonly Vector3[] lagLeg = new Vector3[2], lagLegV = new Vector3[2], lagArm = new Vector3[2], lagArmV = new Vector3[2];
        private Vector3 lagHead, lagHeadV;
        private Quaternion prevRot; private bool primed;
        private Vector3 omega; // velocidade angular do corpo (rad/s, mundo)
        // reagarrar: deslocamento ao longo do antebraço dele + bolha de "soltar e pegar de novo"
        private readonly float[] regripTimer = { 0.9f, 1.6f }, regripT = { 9f, 9f }, regripFrom = new float[2], regripTo = new float[2];
        private readonly Vector3[] lastHandTarget = new Vector3[2];
        // voo
        private bool flight; private float flightT;

        /// <summary>Procura os ossos (nomes do rig da Meshy: Hips, Spine02, LeftUpLeg...). Falso se faltar algum.</summary>
        public bool Bind()
        {
            var all = GetComponentsInChildren<Transform>(true);
            Transform F(string n)
            {
                foreach (var t in all)
                {
                    string s = t.name; int c = s.LastIndexOf(':');
                    if (c >= 0) s = s.Substring(c + 1);
                    if (string.Equals(s, n, System.StringComparison.OrdinalIgnoreCase)) return t;
                }
                return null;
            }
            hips = F("Hips"); spine02 = F("Spine02"); spine01 = F("Spine01"); spine = F("Spine"); neck = F("neck"); head = F("Head"); headEnd = F("head_end");
            string[] side = { "Left", "Right" };
            for (int i = 0; i < 2; i++)
            {
                clav[i] = F(side[i] + "Shoulder"); upper[i] = F(side[i] + "Arm"); fore[i] = F(side[i] + "ForeArm"); hand[i] = F(side[i] + "Hand");
                thigh[i] = F(side[i] + "UpLeg"); shin[i] = F(side[i] + "Leg"); foot[i] = F(side[i] + "Foot"); toe[i] = F(side[i] + "ToeBase");
            }
            IsBound = hips && spine02 && spine01 && spine && neck && head && headEnd;
            for (int i = 0; i < 2 && IsBound; i++)
                IsBound = clav[i] && upper[i] && fore[i] && hand[i] && thigh[i] && shin[i] && foot[i] && toe[i];
            if (!IsBound)
            {
                Debug.LogWarning("[ClearStruggle] Rig da Clear sem os ossos esperados — a luta fica desligada.", this);
                return false;
            }
            for (int i = 0; i < 2; i++)
            {
                armA[i] = Vector3.Distance(upper[i].position, fore[i].position);
                armB[i] = Vector3.Distance(fore[i].position, hand[i].position);
            }
            // A BASE DE CADA QUADRO: a pose de ligação de todos os ossos que este componente posa. Tudo é
            // reposto nela antes de posar (ResetToRest) — a luta nunca depende de o Animator ter reescrito o
            // osso naquele quadro. Sem isso a correção de giro do punho (relativa) somava com a do quadro
            // anterior e a mão oscilava quadro sim, quadro não: piscava.
            posed = new[] { hips, spine02, spine01, spine, neck, head,
                            clav[0], upper[0], fore[0], hand[0], clav[1], upper[1], fore[1], hand[1],
                            thigh[0], shin[0], foot[0], thigh[1], shin[1], foot[1] };
            restLocal = new Quaternion[posed.Length];
            for (int i = 0; i < posed.Length; i++) restLocal[i] = posed[i].localRotation;
            // A PALMA: o osso da mão não diz para onde a palma olha. Recém-instanciado o modelo ainda está
            // na pose de ligação — a pose T da Meshy, palmas PARA BAIXO. Se os braços estiverem mesmo na
            // horizontal, guarda "baixo" no espaço de cada mão; senão não mexe no giro do punho.
            palmKnown = true;
            for (int i = 0; i < 2; i++)
            {
                Vector3 armDir = (hand[i].position - upper[i].position).normalized;
                if (Mathf.Abs(Vector3.Dot(armDir, transform.up)) > 0.35f) palmKnown = false;
                palmLocal[i] = Quaternion.Inverse(hand[i].rotation) * -transform.up;
            }
            Debug.Log("[ClearStruggle] Ligado. Pose T " + (palmKnown ? "confirmada: palmas viram para o braço dele." : "NÃO encontrada: giro do punho desligado."), this);
            return true;
        }

        /// <summary>Um tranco (convulsão): pernas encolhem, tronco dobra, mãos puxam. Soma e decai em ~0,3 s.</summary>
        public void Jolt(float strength) => jolt = Mathf.Min(1.5f, jolt + strength);

        /// <summary>A mão abriu: daqui em diante ela voa, e o próprio componente posa o corpo no LateUpdate.</summary>
        public void BeginFlight()
        {
            flight = true;
            flightT = 0f;
        }

        // ------------------------------------------------------------------------------------
        // CORPO (tronco, cabeça, pernas). Chamado pelo diretor antes de prender o pescoço.
        // ------------------------------------------------------------------------------------
        public void PoseBody(float dt)
        {
            if (!IsBound || dt <= 0f) return;
            ResetToRest(); // cada quadro parte da mesma base (ver Bind)
            Step(dt);
            Vector3 up = transform.up, fwd = transform.forward;
            Vector3 lat = Vector3.Cross(-up, fwd).normalized; // girar em torno dele (+) leva "para baixo" em direção à frente

            float I = sI;
            float effort = Mathf.Max(I, jolt * 0.8f);
            // --- tronco: encolhe/arqueia com os chutes, torce, ofega
            float crunch = 4f + 9f * effort * (0.5f + 0.5f * Mathf.Sin(bodyPhase)) + jolt * 10f + Noise(clock * 0.7f, 21f) * 2f;
            float pant = Mathf.Sin(clock * Mathf.PI * 2f * Mathf.Lerp(1.1f, 2.2f, I)) * (1.5f + 2f * I);
            float twist = 10f * effort * Mathf.Sin(bodyPhase * 0.6f + 1f) + Noise(clock * 0.5f, 22f) * 4f;
            float sideBend = 6f * effort * Mathf.Sin(bodyPhase * 0.8f + 2f) + Noise(clock * 0.4f, 23f) * 2f;
            if (flight)
            {
                float k = Mathf.SmoothStep(0f, 1f, flightT / 0.25f);
                crunch = Mathf.Lerp(crunch, -14f + 6f * Mathf.Sin(flightT * 9f), k); // arqueia para trás no ar
                twist = Mathf.Lerp(twist, 12f * Mathf.Sin(flightT * 7f), k);
            }
            // A COLUNA EM "C": pendurada pelo pescoço, encolher é a bacia vir para a frente — o trecho de
            // baixo inclina para trás (+lat leva "cima" para trás) e o de cima para a frente. "pant" é o
            // peito arfando (todas juntas). Arquear no voo = crunch negativo (o "C" ao contrário).
            Transform[] chain = { hips, spine02, spine01, spine };
            float[] curve = { 0.55f, 0.25f, -0.15f, -0.40f };
            float[] share = { 0.15f, 0.28f, 0.30f, 0.27f };
            for (int i = 0; i < chain.Length; i++)
            {
                Transform child = i + 1 < chain.Length ? chain[i + 1] : neck;
                Vector3 d = Quaternion.AngleAxis(crunch * curve[i] + pant * share[i], lat) * Quaternion.AngleAxis(sideBend * share[i], fwd) * up;
                Aim(chain[i], child, d);
                // a bacia torce ao contrário do peito (quem chuta gira o quadril)
                float tw = (i == 0 ? -0.6f : share[i]) * twist;
                chain[i].rotation = Quaternion.AngleAxis(tw, (child.position - chain[i].position).normalized) * chain[i].rotation;
            }
            // --- pescoço e cabeça: o aperto empurra o queixo para cima; tremor de sufoco; inércia
            float chin = 14f + 10f * Mathf.Max(I, sG * 0.6f) + jolt * 8f + (flight ? 10f : 0f);
            Vector3 headUp = Quaternion.AngleAxis(chin, lat) * up; // positivo em lat leva "baixo" para a frente => "cima" para trás
            headUp = Rotate(lagHead, headUp);
            Vector3 neckDir = Vector3.Slerp(up, headUp, 0.35f);
            Aim(neck, head, neckDir);
            Aim(head, headEnd, headUp);
            float shake = (3f + 7f * I) * Noise(clock * 3.2f, 31f) + 4f * Noise(clock * 0.8f, 32f);
            head.rotation = Quaternion.AngleAxis(shake, headUp) * Quaternion.AngleAxis(3f * I * Noise(clock * 2.7f, 33f), fwd) * head.rotation;

            // --- pernas
            for (int s = 0; s < 2; s++)
            {
                float gate = Mathf.Clamp01((Noise01(clock * 0.9f, seed[s] + 40f) - (0.62f - 0.6f * I)) * 3.5f); // cansada: chuta em rajadas
                float a = Mathf.Clamp01(I * Mathf.Lerp(0.35f, 1f, gate));
                float ph = legPhase[s];
                // REPOUSO DE CORPO PENDURADO (v11): sem força, as pernas não ficam retas e juntas como uma
                // tábua — um joelho dobra mais que o outro, uma perna fica um pouco à frente, e elas derivam
                // devagar. Os chutes somam por cima disso.
                float drift = 6f * Noise(clock * 0.33f, seed[s] + 90f);
                float restHip = (s == 0 ? 15f : 4f) + drift;
                float restKnee = (s == 0 ? 26f : 9f) + drift * 1.4f;
                float hip = restHip + 46f * a * (0.5f + 0.5f * Mathf.Sin(ph)) + jolt * 38f;
                float knee = restKnee + 82f * a * (0.5f + 0.5f * Mathf.Sin(ph + 1.35f)) + jolt * 45f + 14f * sP * Mathf.Abs(Noise(clock * 1.7f, seed[s] + 50f));
                float abd = (s == 0 ? 6f : 3f) + 9f * a * Mathf.Sin(ph * 0.5f + seed[s]) + 3f * Noise(clock * 0.6f, seed[s] + 60f);
                float point = 30f + 12f * a * Mathf.Sin(ph + 2.2f);
                if (flight)
                {
                    // no chicote as pernas saem ESTICADAS PARA TRÁS (longe dele); só depois ela pedala no ar.
                    // Pedalar logo de cara jogava os joelhos para a frente — em cima da cabeça da criatura.
                    float k = Mathf.SmoothStep(0f, 1f, flightT / 0.12f);
                    float pedal = Mathf.SmoothStep(0f, 1f, (flightT - 0.14f) / 0.22f);
                    float fp = flightT * Mathf.PI * 2f * 2.3f + s * Mathf.PI;
                    hip = Mathf.Lerp(hip, Mathf.Lerp(-12f + 4f * s, 22f + 28f * Mathf.Sin(fp), pedal), k);
                    knee = Mathf.Lerp(knee, Mathf.Lerp(20f + 10f * s, 50f + 40f * Mathf.Sin(fp + 1.3f), pedal), k);
                    abd = Mathf.Lerp(abd, 12f + 6f * Mathf.Sin(fp * 0.5f), k);
                }
                Vector3 outward = Vector3.ProjectOnPlane(thigh[s].position - hips.position, up);
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : (s == 0 ? -transform.right : transform.right);
                Vector3 abdAxis = Vector3.Cross(-up, outward).normalized;
                Quaternion legQ = Quaternion.AngleAxis(abd, abdAxis);
                Quaternion lagQ = ToQuat(lagLeg[s]);
                Vector3 axis = lagQ * legQ * lat;
                Vector3 thighDir = lagQ * legQ * (Quaternion.AngleAxis(hip, lat) * -up);
                Vector3 shinDir = ToQuat(lagLeg[s] * 0.45f) * (Quaternion.AngleAxis(-knee, axis) * thighDir);
                Vector3 footDir = Quaternion.AngleAxis(45f - point, axis) * shinDir;
                Aim(thigh[s], shin[s], thighDir);
                Aim(shin[s], foot[s], shinDir);
                Aim(foot[s], toe[s], footDir);
            }
        }

        // ------------------------------------------------------------------------------------
        // BRAÇOS. Pendurada: alvos no pulso dele (do diretor), depois de prender o pescoço.
        // ------------------------------------------------------------------------------------
        /// <param name="leftTarget">Onde a mão esquerda dela agarra (mundo).</param>
        /// <param name="rightTarget">Onde a mão direita agarra.</param>
        /// <param name="along">Direção do antebraço dele, do pulso para o cotovelo (as mãos escorregam/reagarram ao longo dela).</param>
        public void PoseArms(Vector3 leftTarget, Vector3 rightTarget, Vector3 along)
        {
            if (!IsBound || flight) return;
            Vector3 up = transform.up, fwd = transform.forward;
            for (int s = 0; s < 2; s++)
            {
                Vector3 outward = OutwardOf(s, up);
                Vector3 baseT = s == 0 ? leftTarget : rightTarget;
                // puxões e o tremor de quem força a mão dele para fora do pescoço
                float I = sI;
                Vector3 pry = (along * Noise(clock * 3.1f, seed[s] + 70f) * 0.03f + up * Noise(clock * 3.7f, seed[s] + 71f) * 0.025f) * I;
                pry += -along * 0.03f * jolt; // no tranco ela puxa
                // reagarrar: escorrega ao longo do antebraço e a mão sai um pouco de cima dele no meio do gesto
                float u = Mathf.Clamp01(regripT[s] / 0.32f);
                float slide = Mathf.Lerp(regripFrom[s], regripTo[s], Mathf.SmoothStep(0f, 1f, u));
                float lift = Mathf.Sin(Mathf.PI * u) * (u < 1f ? 1f : 0f);
                Vector3 grip = baseT + along * slide + pry + (up * 0.05f + outward * 0.04f) * lift;
                // solta: pende do ombro (com inércia)
                Vector3 S = upper[s].position;
                // PEGADA DE BASTÃO (v16b): o alvo do diretor é o EIXO do antebraço dele. O punho dela encosta AO
                // LADO do braço (do lado do ombro dela), na altura do braço; a palma vira para ele e os dedos
                // fecham por cima. Com o punho em cima do braço, os dedos ficavam para o ar ("pare!").
                Vector3 barCenter = grip;
                Vector3 side = Vector3.ProjectOnPlane(outward, along);
                side = side.sqrMagnitude > 1e-6f ? side.normalized : outward;
                grip = barCenter + side * 0.07f - up * 0.015f;
                float reach = armA[s] + armB[s];
                Vector3 hang = Rotate(lagArm[s], (-up * 0.88f + outward * 0.32f + fwd * 0.12f).normalized) * reach * 0.94f;
                float g = Mathf.Clamp01(sG * (1f - 0.35f * lift));
                Vector3 target = Vector3.Lerp(S + hang, grip, g);
                lastHandTarget[s] = transform.InverseTransformPoint(target); // no espaço dela: no voo a mão vai junto com o corpo
                // ombro sobe quando ela agarra no alto
                ShrugClavicle(s, up, outward, 14f * g + 6f * jolt);
                // cotovelos ABERTOS para os lados (quem puxa uma barra na altura do queixo); com o cotovelo
                // para baixo o antebraço ficava em pé e as mãos juntas pareciam rezar
                Vector3 pole = upper[s].position + Vector3.Lerp(-fwd * 0.4f + outward * 0.5f - up * 0.4f, outward * 1.0f + up * 0.12f - fwd * 0.3f, g);
                SolveArm(s, target, pole);
                Vector3 fdir = (hand[s].position - fore[s].position).normalized;
                // A PALMA VIRA PARA O QUE ELA AGARRA. As referências fixas de antes ("palma para baixo", e a dobra
                // do punho em torno de antebraço×cima) DEGENERAVAM com o antebraço em pé — que é justamente a pose
                // de agarrar um braço na altura do queixo: a projeção virava quase zero, a direção mudava a cada
                // tremor e a mão trocava de lado quadro sim, quadro não (piscava). Agora: agarrada, a palma olha
                // para o EIXO do antebraço dele (o alvo menos a espessura); solta, para a coxa. O peso da correção
                // cai a zero quando a direção fica ambígua (paralela ao antebraço dela), em vez de sortear.
                if (palmKnown)
                {
                    Vector3 toBar = barCenter - hand[s].position;
                    Vector3 want = Vector3.Slerp(-outward, toBar.sqrMagnitude > 1e-6f ? toBar.normalized : -up, g);
                    Vector3 cur = Vector3.ProjectOnPlane(hand[s].rotation * palmLocal[s], fdir);
                    Vector3 des = Vector3.ProjectOnPlane(want, fdir);
                    float clarity = Mathf.Clamp01((des.magnitude - 0.15f) / 0.35f); // 0 = ambíguo, 1 = claro
                    if (cur.sqrMagnitude > 1e-6f && des.sqrMagnitude > 1e-6f)
                    {
                        // continuidade: perto de 180° o sinal vira com qualquer tremor; fica a volta mais perto da anterior
                        float raw = Vector3.SignedAngle(cur, des, fdir) * clarity;
                        while (raw - twistPrev[s] > 180f) raw -= 360f;
                        while (raw - twistPrev[s] < -180f) raw += 360f;
                        raw = Mathf.Clamp(raw, -160f, 160f);
                        float twistDeg = twistPrimed[s] ? Mathf.Lerp(twistPrev[s], raw, 1f - Mathf.Exp(-Time.deltaTime / 0.08f)) : raw;
                        twistPrev[s] = twistDeg; twistPrimed[s] = true;
                        fore[s].rotation = Quaternion.AngleAxis(twistDeg * 0.5f, fdir) * fore[s].rotation;
                        hand[s].rotation = Quaternion.AngleAxis(twistDeg * 0.5f, fdir) * hand[s].rotation;
                    }
                    // o punho dobra PARA O LADO DA PALMA (fecha sobre o braço dele) — eixo = dedos × palma, nunca degenera
                    Vector3 palmN = hand[s].rotation * palmLocal[s];
                    Vector3 flexAxis = Vector3.Cross(fdir, palmN);
                    if (flexAxis.sqrMagnitude > 1e-4f)
                        hand[s].rotation = Quaternion.AngleAxis(70f * g, flexAxis.normalized) * hand[s].rotation; // os dedos fecham por cima do braço dele
                }
            }
        }

        private void LateUpdate()
        {
            if (!IsBound || !flight) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            flightT += dt;
            PoseBody(dt);
            Vector3 up = transform.up, fwd = transform.forward;
            Vector3 lat = Vector3.Cross(-up, fwd).normalized;
            float k = Mathf.SmoothStep(0f, 1f, flightT / 0.18f); // larga o pulso dele e abre os braços
            for (int s = 0; s < 2; s++)
            {
                Vector3 outward = OutwardOf(s, up);
                float w = flightT * 360f * 1.35f + (s == 0 ? 0f : 140f);
                Vector3 dir = Quaternion.AngleAxis(w, lat) * (up * 0.45f + outward * 0.9f).normalized;
                dir = Vector3.Slerp((outward + up * 0.6f).normalized, dir, 0.55f);
                dir = Rotate(lagArm[s], dir);
                Vector3 S = upper[s].position;
                Vector3 flail = S + dir * (armA[s] + armB[s]) * 0.86f;
                Vector3 target = Vector3.Lerp(transform.TransformPoint(lastHandTarget[s]), flail, k);
                ShrugClavicle(s, up, outward, 10f);
                SolveArm(s, target, S - fwd * 0.5f - up * 0.3f + outward * 0.3f);
            }
        }

        // ------------------------------------------------------------------------------------
        private void Step(float dt)
        {
            clock += dt;
            float follow = 1f - Mathf.Exp(-dt / 0.12f);
            sI = Mathf.Lerp(sI, flight ? 1f : Mathf.Clamp01(Intensity), follow);
            sG = Mathf.Lerp(sG, flight ? 0f : Mathf.Clamp01(Grip), 1f - Mathf.Exp(-dt / 0.09f));
            sP = Mathf.Lerp(sP, flight ? 1f : Mathf.Clamp01(Passive), follow);
            jolt *= Mathf.Exp(-dt / 0.28f);

            // fase dos chutes: cada perna no próprio ritmo, que oscila (nunca um metrônomo)
            for (int s = 0; s < 2; s++)
            {
                float f = Mathf.Lerp(0.7f, 2.9f, sI) * (1f + 0.3f * Noise(clock * 0.45f, seed[s] + 10f));
                legPhase[s] += Mathf.PI * 2f * f * dt;
            }
            bodyPhase += Mathf.PI * 2f * Mathf.Lerp(0.5f, 1.6f, sI) * dt;

            // velocidade angular do corpo (o pêndulo e o giro do voo) -> inércia dos membros
            Quaternion rot = transform.rotation;
            if (primed)
            {
                (rot * Quaternion.Inverse(prevRot)).ToAngleAxis(out float ang, out Vector3 ax);
                if (ang > 180f) ang -= 360f;
                Vector3 w = float.IsNaN(ax.x) || float.IsInfinity(ax.x) ? Vector3.zero : ax * (ang * Mathf.Deg2Rad / dt);
                omega = Vector3.Lerp(omega, w, 1f - Mathf.Exp(-dt / 0.05f));
            }
            prevRot = rot; primed = true;
            float gain = Mathf.Lerp(0.05f, 0.13f, sP);
            Vector3 target = Vector3.ClampMagnitude(-omega * gain, 0.85f);
            for (int s = 0; s < 2; s++)
            {
                Spring(ref lagLeg[s], ref lagLegV[s], target, 2.1f, 0.32f, dt);
                Spring(ref lagArm[s], ref lagArmV[s], target * 1.2f, 1.8f, 0.3f, dt);
            }
            Spring(ref lagHead, ref lagHeadV, target * 0.5f, 3.0f, 0.45f, dt);

            // reagarrar: com força para isso, de tempos em tempos uma das mãos troca de lugar
            for (int s = 0; s < 2; s++)
            {
                regripT[s] += dt;
                regripTimer[s] -= dt * Mathf.Lerp(0.2f, 1f, sI);
                if (regripTimer[s] <= 0f && sI > 0.45f && sG > 0.8f)
                {
                    regripFrom[s] = Mathf.Lerp(regripFrom[s], regripTo[s], 1f);
                    regripTo[s] = Mathf.Clamp(regripFrom[s] + (Noise(clock * 5f, seed[s] + 80f) > 0f ? 0.06f : -0.05f), -0.03f, 0.10f);
                    regripT[s] = 0f;
                    regripTimer[s] = 0.9f + 0.9f * Noise01(clock * 3f, seed[s] + 81f);
                }
            }
        }

        private void SolveArm(int s, Vector3 target, Vector3 polePoint)
        {
            Vector3 S = upper[s].position;
            float a = armA[s], b = armB[s];
            Vector3 st = target - S;
            float dist = Mathf.Clamp(st.magnitude, 0.05f, (a + b) * 0.995f);
            Vector3 dir = st.sqrMagnitude > 1e-8f ? st.normalized : -transform.up;
            float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 pole = Vector3.ProjectOnPlane(polePoint - S, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(-transform.up, dir);
            pole.Normalize();
            Vector3 elbow = S + dir * (a * cosA) + pole * (a * sinA);
            Aim(upper[s], fore[s], (elbow - S).normalized);
            Aim(fore[s], hand[s], (S + dir * dist - fore[s].position).normalized);
        }

        private void ShrugClavicle(int s, Vector3 up, Vector3 outward, float deg)
        {
            // AngleAxis(+θ, outward×up) leva "para fora" em direção a "cima": o ombro sobe
            Vector3 axis = Vector3.Cross(outward, up);
            if (axis.sqrMagnitude > 1e-6f)
                clav[s].rotation = Quaternion.AngleAxis(deg, axis.normalized) * clav[s].rotation;
        }

        private Vector3 OutwardOf(int s, Vector3 up)
        {
            Vector3 o = Vector3.ProjectOnPlane(upper[s].position - neck.position, up);
            return o.sqrMagnitude > 1e-6f ? o.normalized : (s == 0 ? -transform.right : transform.right);
        }

        /// <summary>Gira o osso para que a direção dele (até o filho) vire <paramref name="dir"/>, mantendo o resto da pose.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-10f || dir.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        private static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float hz, float zeta, float dt)
        {
            float w = 2f * Mathf.PI * hz;
            v += ((target - x) * (w * w) - v * (2f * zeta * w)) * dt;
            x += v * dt;
            x = Vector3.ClampMagnitude(x, 0.9f);
        }

        private static Quaternion ToQuat(Vector3 rv)
        {
            float ang = rv.magnitude;
            return ang < 1e-5f ? Quaternion.identity : Quaternion.AngleAxis(ang * Mathf.Rad2Deg, rv / ang);
        }

        private static Vector3 Rotate(Vector3 rv, Vector3 v) => ToQuat(rv) * v;
        private static float Noise(float x, float y) => Mathf.PerlinNoise(x, y) * 2f - 1f;
        private static float Noise01(float x, float y) => Mathf.PerlinNoise(x, y);
    }
}
