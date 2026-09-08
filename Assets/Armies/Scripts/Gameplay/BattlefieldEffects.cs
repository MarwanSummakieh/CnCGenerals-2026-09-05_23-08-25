using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonFrontier
{
    /// <summary>Bounded shared particle emitters and projectiles. Combat damage lands with the visible impact.</summary>
    public sealed class BattlefieldEffects : MonoBehaviour
    {
        sealed class Projectile
        {
            public LineRenderer Trail;
            public RtsUnit Attacker, Target;
            public Vector3 From, To, Previous;
            public float Age, Duration, Damage, Splash, Size, Arc;
            public int Team;
            public bool Active, Missile;
        }
        sealed class Scar
        {
            public GameObject Root;
            public float Life;
        }
        readonly List<Projectile> projectiles = new List<Projectile>(96);
        readonly List<Scar> scars = new List<Scar>(48);
        readonly List<Object> owned = new List<Object>();
        readonly List<RtsUnit> victims = new List<RtsUnit>(32);
        SkirmishSimulation simulation;
        ParticleSystem smoke, dust, flame, sparks, debris;
        Material tracerMaterial, scarMaterial;
        AudioSource sound;
        AudioClip cannonSound, gunSound, explosionSound, missileSound;
        int activeShells;
        public int ActiveProjectiles => activeShells;
        public int ImpactCount { get; private set; }

        public void Initialize(SkirmishSimulation owner)
        {
            simulation = owner;
            Shader shader = Resources.Load<Shader>("BattlefieldParticle");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var cloud = MakeTexture(true);
            var glow = MakeTexture(false);
            Material CloudMaterial(string name, bool additive, Texture texture)
            {
                var m = new Material(shader) { name = name, mainTexture = texture };
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                owned.Add(m); return m;
            }
            var haze = CloudMaterial("Soft smoke and dust", false, cloud);
            var heat = CloudMaterial("Incandescent fire and sparks", true, glow);
            smoke = MakeEmitter("Rolling smoke", haze, 1500, 0, 2.7f, false);
            dust = MakeEmitter("Ground dust", haze, 1000, .02f, 2.4f, false);
            flame = MakeEmitter("Muzzle fire and explosion", heat, 600, -.15f, 1.8f, false);
            sparks = MakeEmitter("Hot fragments", heat, 700, 1.2f, .25f, true);
            debris = MakeEmitter("Blast debris", haze, 400, 1.6f, .65f, true);
            tracerMaterial = CloudMaterial("Hot tracer", true, Texture2D.whiteTexture);
            scarMaterial = CloudMaterial("Impact scorch", false, cloud);
            sound = gameObject.AddComponent<AudioSource>(); sound.spatialBlend = 0; sound.playOnAwake = false;
            cannonSound = MakeSound("Cannon report", .65f, 58, .8f);
            gunSound = MakeSound("Automatic fire", .15f, 145, .6f);
            explosionSound = MakeSound("Explosion rumble", 1.5f, 34, 1f);
            missileSound = MakeSound("Rocket ignition", .45f, 220, .4f);
        }

        Texture2D MakeTexture(bool cloud)
        {
            const int n = 128;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = cloud ? "Original organic smoke" : "Original soft flame", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n, v = (y + .5f) / n;
                float r = new Vector2(u - .5f, v - .5f).magnitude * 2;
                float noise = .52f + .48f * Mathf.PerlinNoise(u * 7 + 23, v * 7 + 51);
                float a = cloud ? Mathf.Pow(Mathf.Clamp01((1f - r) * noise * 1.8f), 1.55f) : Mathf.Pow(Mathf.Clamp01(1f - r), 2.3f);
                pixels[y * n + x] = new Color(1, 1, 1, a);
            }
            texture.SetPixels(pixels); texture.Apply(true, true); owned.Add(texture); return texture;
        }

        ParticleSystem MakeEmitter(string name, Material material, int cap, float gravity, float growth, bool stretch)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = 60;
            main.maxParticles = cap; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0; main.gravityModifier = gravity; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(.22f, 1), new Keyframe(1, growth)));
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .07f), new GradientAlphaKey(.65f, .4f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var rotation = ps.rotationOverLifetime; rotation.enabled = !stretch;
            rotation.z = new ParticleSystem.MinMaxCurve(-.2f, .2f);
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = stretch ? 1.8f : 1; renderer.velocityScale = stretch ? .12f : 0;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.maxParticleSize = .3f;
            ps.Play(); return ps;
        }

        void Emit(ParticleSystem emitter, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size, startLifetime = life,
                startColor = color, rotation = Random.Range(0, 360) };
            emitter.Emit(p, 1);
        }

        public void Fire(RtsUnit attacker, RtsUnit target, float damage, float splash)
        {
            Vector3 from = attacker.Position + Vector3.up * Mathf.Min(attacker.ScreenHeight * .6f, 4);
            Vector3 to = target.Position + Vector3.up * Mathf.Min(target.ScreenHeight * .45f, 3);
            bool rifle = attacker.Key == "Rifle" || attacker.Key == "Scout" || attacker.Key == "APC" || attacker.Key == "Commando" || attacker.Key == "Engineer" || attacker.Key == "Drone";
            bool rocket = attacker.Key == "Rocket" || attacker.Key == "AntiAir" || attacker.Key == "AirDefense" || attacker.Key == "Helicopter" || attacker.Key == "Fighter";
            float size = attacker.Key == "Superweapon" ? 7f : attacker.Key == "Artillery" || attacker.Key == "Bomber" ? 3.2f : rifle ? .35f : 1.7f;
            from += (to - from).normalized * (attacker.IsStructure ? .5f : attacker.Radius * .8f);
            if (simulation.IsPointVisible(from))
            {
                for (int i = 0; i < (rifle ? 2 : 5); i++)
                    Emit(flame, from, (to - from).normalized * Random.Range(1, 6), rifle ? 1.2f : 2.9f, .09f + Random.value * .07f, new Color(1, .7f, .2f));
                Emit(smoke, from, Vector3.up * .7f, rifle ? .5f : 1.4f, 1.4f, new Color(.62f, .58f, .5f, .3f));
                Play(rocket ? missileSound : rifle ? gunSound : cannonSound, from, rifle ? .13f : .35f);
            }
            Projectile p = projectiles.Find(candidate => !candidate.Active);
            if (p == null)
            {
                // Preserve damage even when the strictly bounded visual pool is saturated.
                if (projectiles.Count >= 96) { Resolve(attacker, target, to, damage, splash, attacker.Team); return; }
                var root = new GameObject("Shell / missile flight"); root.transform.SetParent(transform, false);
                var line = root.AddComponent<LineRenderer>(); line.sharedMaterial = tracerMaterial;
                line.positionCount = 2; line.useWorldSpace = true; line.shadowCastingMode = ShadowCastingMode.Off;
                line.numCapVertices = 2;
                p = new Projectile { Trail = line }; projectiles.Add(p);
            }
            p.Active = true; p.Age = 0; p.Attacker = attacker; p.Target = target; p.From = p.Previous = from; p.To = to;
            p.Damage = damage; p.Splash = splash; p.Team = attacker.Team; p.Size = size; p.Missile = rocket;
            p.Duration = Mathf.Clamp(Vector3.Distance(from, to) / (rifle ? 220 : rocket ? 38 : 65), .055f, 1.65f);
            p.Arc = attacker.Key == "Artillery" || attacker.Key == "Superweapon" ? Vector3.Distance(from, to) * .24f : attacker.Key == "Bomber" ? 2f : 0;
            p.Trail.enabled = true; p.Trail.widthMultiplier = rifle ? .085f : rocket ? .22f : .18f;
            p.Trail.startColor = new Color(1, .8f, .35f, .15f); p.Trail.endColor = new Color(1, .94f, .72f);
            p.Trail.SetPosition(0, from); p.Trail.SetPosition(1, from); activeShells++;
            attacker.GetComponent<UnitPresentation>()?.Recoil();
        }

        public void Tick(float dt)
        {
            foreach (var p in projectiles)
            {
                if (!p.Active) continue;
                p.Age += dt;
                if (p.Missile && p.Target && p.Target.IsAlive) p.To = p.Target.Position + Vector3.up * Mathf.Min(p.Target.ScreenHeight * .45f, 3);
                float t = Mathf.Clamp01(p.Age / p.Duration);
                Vector3 position = Vector3.Lerp(p.From, p.To, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * p.Arc;
                Vector3 direction = (position - p.Previous).normalized;
                p.Trail.SetPosition(0, position - direction * Mathf.Min(p.Missile ? 2 : 3.5f, Vector3.Distance(p.From, position)));
                p.Trail.SetPosition(1, position);
                p.Trail.enabled = simulation.IsPointVisible(position);
                if (p.Trail.enabled && (p.Missile || p.Arc > 0))
                {
                    Emit(smoke, position, Vector3.up * .4f, p.Missile ? .65f : .4f, 1.8f, new Color(.72f, .72f, .66f, .46f));
                    Emit(flame, position - direction * .25f, -direction, .7f, .12f, new Color(1, .55f, .12f));
                }
                p.Previous = position;
                if (t < 1) continue;
                p.Active = false; p.Trail.enabled = false; activeShells--;
                Impact(p.To, p.Size, false);
                Resolve(p.Attacker, p.Target, p.To, p.Damage, p.Splash, p.Team);
                p.Attacker = p.Target = null;
            }
            foreach (var scar in scars)
                if (scar.Life > 0 && (scar.Life -= dt) <= 0) scar.Root.SetActive(false);
        }

        void Resolve(RtsUnit attacker, RtsUnit target, Vector3 impact, float damage, float splash, int team)
        {
            if (target && target.IsAlive && new Vector2(target.Position.x - impact.x, target.Position.z - impact.z).sqrMagnitude <= Mathf.Pow(target.Radius + 1f, 2))
                simulation.ReceiveDamage(target, damage, attacker);
            if (splash <= 0) return;
            victims.Clear();
            foreach (var unit in simulation.Units)
                if (unit && unit != target && unit.IsAlive && unit.Team != team && !unit.IsAircraft &&
                    new Vector2(unit.Position.x - impact.x, unit.Position.z - impact.z).sqrMagnitude < splash * splash) victims.Add(unit);
            foreach (var unit in victims) simulation.ReceiveDamage(unit, damage * .4f, attacker);
        }

        public void Destruction(RtsUnit unit)
        {
            Impact(unit.Position + Vector3.up * .7f, unit.IsStructure ? unit.Radius * .75f : unit.Definition.category == "Infantry" ? .75f : 3.3f, true);
        }

        void Impact(Vector3 position, float size, bool wreck)
        {
            ImpactCount++;
            if (!simulation.IsPointVisible(position)) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(size * 6), 3, 48);
            for (int i = 0; i < count; i++)
            {
                Vector3 spread = Random.insideUnitSphere;
                Emit(flame, position + spread * size * .35f, spread * size * .65f + Vector3.up * 1.8f,
                    size * Random.Range(.8f, 1.7f), Random.Range(.25f, .6f), new Color(1, Random.Range(.25f, .6f), .035f));
                Emit(sparks, position, (spread + Vector3.up * .7f) * Random.Range(4, 14) * Mathf.Sqrt(size),
                    Random.Range(.12f, .27f), Random.Range(.3f, 1.2f), new Color(1, .69f, .23f));
                if (i % 2 == 0)
                {
                    Emit(smoke, position + spread * size * .45f, spread * .7f + Vector3.up * Random.Range(1, 2.7f),
                        size * Random.Range(.9f, 1.8f), Random.Range(3.5f, wreck ? 10 : 5), new Color(.2f, .205f, .19f, .65f));
                    Emit(debris, position, (spread + Vector3.up) * Random.Range(3, 8), .23f * Mathf.Sqrt(size), 1.4f, new Color(.22f, .2f, .15f));
                }
                Vector3 ground = position; ground.y = DemoBattlefield.GroundHeight(position) + .25f;
                Emit(dust, ground, new Vector3(spread.x, .15f, spread.z) * size * 2,
                    size * 1.4f, 2.5f, new Color(.59f, .48f, .33f, .35f));
            }
            if (size > 1) { Play(explosionSound, position, Mathf.Clamp(size * .075f, .1f, .5f)); Scorch(position, size * (wreck ? 1.8f : 1)); }
        }

        void Scorch(Vector3 position, float size)
        {
            if (Mathf.Abs(position.x - DemoBattlefield.RiverCenter(position.z)) < DemoBattlefield.RiverHalfWidth || position.y > 7) return;
            Scar scar = scars.Find(s => s.Life <= 0);
            if (scar == null)
            {
                if (scars.Count >= 48) scar = scars[0];
                else
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Ground blast scorch";
                    go.transform.SetParent(transform, false); var collider = go.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                    var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = scarMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                    // Vertex colour survives the unlit particle shader without per-instance materials.
                    var mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh); var colors = new Color[mesh.vertexCount];
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color(.06f, .052f, .038f, .65f);
                    mesh.colors = colors; owned.Add(mesh); go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    scar = new Scar { Root = go }; scars.Add(scar);
                }
            }
            scar.Root.SetActive(true); scar.Life = 45;
            scar.Root.transform.position = new Vector3(position.x, DemoBattlefield.GroundHeight(position) + .045f, position.z);
            scar.Root.transform.rotation = Quaternion.Euler(90, Random.Range(0, 360), 0); scar.Root.transform.localScale = Vector3.one * size;
        }

        public void VehicleDust(Vector3 at, float radius)
        {
            Emit(dust, at + Vector3.up * .2f, new Vector3(.2f, .3f, .1f), radius * 1.4f, 2, new Color(.66f, .56f, .4f, .25f));
        }
        public void DamageSmoke(Vector3 at, float size, bool burning)
        {
            Emit(smoke, at, new Vector3(.25f, 1.7f, .15f), size, 4.8f, new Color(.16f, .17f, .16f, .55f));
            if (burning) Emit(flame, at - Vector3.up * .3f, Vector3.up * 1.2f, size * .75f, .5f, new Color(1, .3f, .03f));
        }
        public void Welding(Vector3 at)
        {
            Emit(flame, at, Vector3.up, .9f, .13f, new Color(.65f, .82f, 1));
            for (int i = 0; i < 3; i++) Emit(sparks, at, Random.insideUnitSphere * 3 + Vector3.up * 2, .12f, .6f, new Color(1, .76f, .28f));
        }

        AudioClip MakeSound(string name, float duration, float hz, float noiseAmount)
        {
            const int rate = 22050; var data = new float[Mathf.CeilToInt(duration * rate)];
            var random = new System.Random(name.Length * 113); float filtered = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate, envelope = Mathf.Exp(-t * 7 / duration) * Mathf.Min(1, t * 1000);
                float noise = (float)random.NextDouble() * 2 - 1; filtered = Mathf.Lerp(filtered, noise, .16f);
                data[i] = Mathf.Clamp((Mathf.Sin(t * hz * Mathf.Exp(-t * 3) * Mathf.PI * 2) * .32f + filtered * noiseAmount + noise * noiseAmount * .16f) * envelope, -1, 1);
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false); clip.SetData(data, 0); owned.Add(clip); return clip;
        }
        void Play(AudioClip clip, Vector3 at, float volume)
        {
            var camera = Camera.main; if (!camera || !sound) return;
            Vector3 view = camera.WorldToViewportPoint(at);
            float distance = Mathf.Max(Mathf.Abs(view.x - .5f) * 2, Mathf.Abs(view.y - .5f) * 2);
            sound.panStereo = Mathf.Clamp((view.x - .5f) * 1.2f, -.8f, .8f);
            sound.PlayOneShot(clip, volume / (1 + distance * distance * 3));
        }
        void OnDestroy() { foreach (var obj in owned) if (obj) Destroy(obj); owned.Clear(); }
    }
}
